# Recording and identifier protection

LecturIA encrypts every recording at rest and pseudonymizes student
identifiers in every file the application writes to disk. The application
binary holds an RSA public key and a 32-byte UID key; the matching RSA
private key never ships with the application, while the UID key is
embedded in the binary and treated as obfuscation-grade material (see
"Threat model" below).

## Threat model

What this design protects against:

- A stolen laptop, lost USB drive, or compromised cloud sync containing
  the recordings folder. The attacker cannot recover the audio.
- A teacher or third party with full read access to the local machine
  trying to play back recordings outside the supervised workflow.
- In-flight tampering: any byte modified inside an encrypted frame, in
  the file header, or anywhere bound to the AAD makes decryption fail.
- Casual disclosure of student identity through file listings,
  screenshots, accidental sharing, or backups: file names contain only
  opaque UIDs and the persistent course list contains only UIDs.

What this design does NOT protect against:

- Memory access during capture and session-only playback. PCM and MP3 buffers
  exist in process memory while a recording is in progress. The MP3 bytes from
  the latest successfully completed recording remain in memory so the teacher
  can verify it until another recording replaces them, the user signs out, or
  the application exits. An attacker with administrative privileges on the
  recording machine could in principle dump these buffers.
- Forgery. The public key is embedded in the application binary and can
  be extracted by anyone who has the binary. With the public key alone,
  an attacker can produce a syntactically valid `.lra` file containing
  arbitrary audio. Decryption will succeed and the operator will get
  whatever audio the attacker chose to embed. Authenticity (proving that
  a `.lra` was produced by the official application running on a
  legitimate device) requires per-device signing keys and is out of scope
  for this version.
- A determined adversary recovering plain student metadata from a UID.
  The UID key is embedded in the binary; extracting it requires
  reverse-engineering effort but is feasible. See the dedicated
  "Pseudonymization caveats" section below.
- Loss of the private key. There is no recovery mechanism. If the private
  key is lost, every recording produced under the matching public key is
  permanently unrecoverable.

## Cryptographic construction

Hybrid public-key + symmetric AEAD:

| Layer | Primitive | Notes |
|-------|-----------|-------|
| Key wrap | RSA-4096 with OAEP-SHA256 padding | One wrap per file. |
| Content | AES-256-GCM | 12-byte nonce, 16-byte tag, 64 KiB plaintext frames. |
| Nonce derivation | 8 random bytes (per file) `||` 4-byte big-endian counter | Counter increments per frame. |
| AAD | SHA-256 of the file header | Binds frames to the file metadata. |

For each new recording the application:

1. Generates a fresh 32-byte AES-256 key and an 8-byte random nonce
   prefix.
2. Wraps the AES key under the embedded RSA public key (`Encrypt` with
   `RSAEncryptionPadding.OaepSHA256`).
3. Writes the file header (see layout below) and computes its SHA-256.
4. Encrypts the inner audio (today, an MP3 stream produced by NAudio +
   libmp3lame at 16 kHz mono 64 kbps) frame by frame. Each frame uses a
   unique nonce derived from the random prefix and a 32-bit counter, and
   the SHA-256 of the header as associated data.
5. Writes a zero-length end sentinel to mark end of stream and detect
   truncation.

## Session-only playback

During capture, the MP3 encoder writes each byte to both the encrypting stream
and a pending in-memory buffer. The application promotes that buffer for
playback only after the MP3 writer and encrypted container finalize
successfully. A failed capture or finalization leaves the previous successful
session sample unchanged.

Only the latest successfully completed recording is retained. Replacing the
sample, signing out, or disposing the recorder overwrites its byte array before
releasing it. The plaintext MP3 is never written to disk. Closing LecturIA ends
the process and makes the sample unavailable; the application cannot recreate
it from an existing `.lra` because the private decryption key is intentionally
absent from the recorder.

## File format (`.lra` v2)

```text
Header:
  magic            "LRA1"             4 bytes
  version          0x02               1 byte
  flags            0x00               1 byte (reserved)
  pk_fingerprint   SHA-256(SPKI)[:8]  8 bytes
  wrapped_key_len  uint32 BE          4 bytes
  wrapped_key      RSA-OAEP-SHA256    wrapped_key_len bytes (512 for RSA-4096)
  nonce_prefix     random             8 bytes
  chunk_size       uint32 BE          4 bytes
  inner_mime_len   uint16 BE          2 bytes
  inner_mime       UTF-8              inner_mime_len bytes  ("audio/mpeg")
  metadata_len     uint32 BE          4 bytes            (added in v2)
  metadata         UTF-8 JSON         metadata_len bytes  (added in v2; may be 0)

Frame (zero or more, in order):
  ct_len           uint32 BE          4 bytes (1..chunk_size)
  ciphertext       AES-256-GCM        ct_len bytes
  tag              GCM tag            16 bytes

End sentinel:
  ct_len           uint32 BE = 0      4 bytes
```

The format is canonicalized so that re-hashing the header on the reader
side yields the same AAD bytes. Because the metadata block is part of the
header, it is covered by the AAD and therefore tamper-evident: altering it
makes every frame fail authentication. The metadata is plaintext (like the
MIME type), so it is readable without the private key; it must only ever
carry non-sensitive descriptive data. The reader verifies the public key
fingerprint matches the supplied private key before attempting any
decryption.

### Header metadata (v2)

The metadata block is a compact UTF-8 JSON object identifying which reading
passage the audio corresponds to, so downstream processing (transcription,
scoring) can match audio to text:

```json
{ "schema": 1, "textId": "el_paseo_n1" }
```

| Field    | Type   | Meaning                                   |
|----------|--------|-------------------------------------------|
| `schema` | int    | Metadata schema version (currently 1).    |
| `textId` | string | Primary key of the reading passage.       |

Only the primary key is stored, to keep the payload small. `textId` is a
governed identifier of the form `<slug>_n<level>` (for example
`el_paseo_n1`), from which a consumer resolves the passage title, level,
and body via the reading text catalog for the matching release. The id
construction and uniqueness rules are the authoritative contract for
external consumers.

New fields may be added under the same container version 2 by bumping
`schema`; readers should ignore unknown fields and tolerate a missing
metadata block (length 0).

### Version compatibility

Version 1 files have no metadata block and end the header at `inner_mime`.
A reader must branch on the version byte: only parse the metadata block
when `version >= 2`. The standalone decryption tool (maintained separately)
must be updated to read v2 before it can open recordings produced by this
version; v1 files remain readable unchanged.

The format constants live in
`src/LecturIA.Core/Crypto/EncryptedRecordingFormat.cs`. The same constants
are duplicated, on purpose, in the standalone decryption tool so the two
implementations are independent.

## Key ceremony

The application owner performs the key ceremony once per release line.
The same tool generates both the RSA pair (used for recording content
encryption) and the UID key (used for student identifier
pseudonymization). The tool is idempotent: re-running it does not
overwrite files that already exist unless `--force` is set.

1. Generate the keys. Choose paths that are outside the repository root
   for the private key and pick any path inside or outside the repo for
   the UID key file (it is gitignored either way, but the build expects
   it at `src/LecturIA.App/Assets/uid-key.txt`).

   ```pwsh
   dotnet run --project tools/keys/LecturIA.GenerateKeys -- `
       --public-key  src/LecturIA.App/Assets/public-key.pem `
       --private-key <path-outside-repo>\private-key.pem `
       --uid-key     src/LecturIA.App/Assets/uid-key.txt
   ```

   The tool prints two fingerprints (first 8 bytes of SHA-256, hex): one
   for the RSA public key, one for the UID key. Record both. The RSA
   fingerprint appears in every recording header; the UID fingerprint
   identifies which UID key was used to produce a given UID.

2. Move the private key off the working machine. Recommended targets, in
   decreasing order of preference:
   - A password manager that supports secure file attachments (1Password,
     Bitwarden Premium).
   - An encrypted USB drive stored in a physically secure location.
   - A hardware security module or smart card that supports importing a
     PKCS#8 RSA-4096 private key.

3. Keep a copy of the UID key (`uid-key.txt`) in the same custody as the
   private key. Linked tools that decode UIDs need it. The application
   binary has its own copy embedded at build time, so the local file is
   only needed for the build itself and for downstream tooling.

4. Delete the local copies of the private key and any standalone copy of
   the UID key after the move is verified.

5. Commit the public key (`src/LecturIA.App/Assets/public-key.pem`).
   Build and ship the application as usual. Both keys are embedded in
   the executable as manifest resources and loaded once at startup.

The repository `.gitignore` blocks all `.pem` files by default and opens
only `public-key.pem` as an explicit exception. It also blocks `.p12`,
`.pfx`, `.key` files, and `uid-key.txt` as a safety net.

## Decryption

The decryption tool is maintained in a **separate private repository**
(`lecturia-decrypt`) that is not publicly accessible. Only the
application owner and authorized personnel with access to that repository
and the RSA private key can decrypt recordings.

The tool accepts the private key path, an input `.lra` file, and an
output path. It:

1. Reads the private key from PEM (PKCS#8).
2. Parses the file header and verifies that the SHA-256 fingerprint of
   the supplied private key matches the fingerprint stored in the file.
3. Unwraps the AES key with RSA-OAEP-SHA256.
4. Decrypts each frame with AES-256-GCM, using the SHA-256 of the
   verbatim header bytes as associated data. A wrong private key, a
   corrupted file, or a tampered frame causes an authentication failure.

If the inner MIME is `audio/mpeg`, the recovered file is a regular MP3
that any media player can open.

## Key rotation

When a key needs to be rotated (suspected compromise, scheduled
rotation):

1. Run the key ceremony again to generate a new key pair. Pass `--force`
   if a public key already exists at the target path.
2. Update the application binary and ship a new release. New recordings
   carry the new fingerprint in their headers.
3. Keep the previous private key archived. Old `.lra` files still in
   circulation can only be decrypted with their original private key,
   identified by the fingerprint embedded in the header.

There is intentionally no in-binary mechanism to revoke or invalidate
old public keys. Rotation is operational, not cryptographic.

# Identifier pseudonymization

Student data uses two related opaque identifiers derived from the same UID
master key:

- The persistent course list stores a reversible student UID containing the
  normalized RUT, first name, and last name. The application decodes it only
  in memory when loading a course.
- New recording file names use a one-way identifier derived only from the
  normalized RUT. This keeps the RUT primary key stable when a student's name
  is corrected. Legacy recording file names remain readable and are matched
  by decoding their student UID in memory.

## Persistent course UID construction

A two-key SIV-style scheme built on .NET 8 native primitives:

```text
K_uid                 = 32 random bytes (the UID master key)
K_enc                 = SHA-256("LECTURIA-UID-ENC" || K_uid)
K_mac                 = SHA-256("LECTURIA-UID-MAC" || K_uid)

payload(rut,first,last) = [1B len][rut UTF-8][1B len][first UTF-8]
                          [1B len][last UTF-8]

tag                   = HMAC-SHA256(K_mac, payload)[:16]
ciphertext            = AES-256-CTR(K_enc, IV = tag, payload)
UID                   = base32_no_padding(tag || ciphertext)
```

Decryption recovers `payload` by running CTR with the same `tag` as the
initial counter, recomputes `tag` from the recovered payload, and
rejects the UID with a cryptographic exception when the freshly computed
tag does not match. The construction is deterministic (same student
always yields the same UID), authenticated (any modification of the UID
is detected at decode time), and uses only audited primitives.

UID length is roughly `ceil((16 + payload_length) * 8 / 5)`, which is
about 60-90 base32 characters for typical Chilean records.

## Recording identifier construction

New recording names use a keyed one-way pseudonym of the RUT:

```text
normalized_rut         = uppercase(remove_dots_and_spaces(rut))
K_recording            = HMAC-SHA256(K_uid, "LECTURIA-RECORDING-RUT-ID")
digest                 = HMAC-SHA256(K_recording, UTF8(normalized_rut))
recording_id           = "R1-" || base32_no_padding(digest)
```

Only the RUT contributes to this identifier. The same RUT therefore keeps
the same recording identifier if a first name or last name changes. The
identifier is not decrypted by the application. Instead, the application
recomputes it from each in-memory roster RUT and compares the result with
completed recording file names.

The `R1-` prefix distinguishes this format from legacy reversible UIDs.
When the completion index encounters a legacy file, it decodes that UID in
memory, derives the new RUT-only identifier, and discards the decoded value.
This keeps existing recordings visible in the interface without renaming
files or writing plaintext PII.

## Current evaluation retention

Each student RUT may have only one current evaluation in the recordings folder.
Replacement follows a save-first policy so a failed capture cannot destroy the
student's last valid evaluation:

1. The application writes and finalizes the new encrypted `.lra` file using a
   collision-safe path.
2. After successful finalization, it verifies that the new file is inside the
   active recordings folder, is a finalized container, and resolves to the
   selected student's RUT-derived recording identifier.
3. It then deletes every other current or legacy recording name that resolves
   to the same RUT, preserving only the newly completed file.

If an older file cannot be deleted, the new evaluation remains intact and the
interface reports that replacement cleanup was incomplete. Existing historical
duplicates are not deleted automatically at startup; they are consolidated only
after that student is successfully evaluated again.

## What lands on disk

- New recording files: `<R1-RUT_PSEUDONYM>.lra` under
  `%USERPROFILE%\Desktop\Grabaciones LecturIA\`. Collision-safe creation may
  append `_1`, `_2`, and so on while a previous evaluation still exists. After
  a successful replacement, the newest file is retained and may keep that
  suffix. Plain RUT or name does not appear in the file name.
- Legacy recording files: `<UID>.lra`. The application continues to decode
  these names in memory when building completion status; files are not renamed.
- Persistent course list: `%LOCALAPPDATA%\LecturIA\courses.json`. Each
  course stores its school, level, section, and a list of UIDs only.
- Legacy file `%LOCALAPPDATA%\LecturIA\names.json` (which contained PII
  in plain text) is migrated and deleted on first launch of the new
  binary.

## Imported planillas

Imported planillas (XLSX or CSV) keep PII at import time, by design: the
application owner has explicitly accepted that the input file is the
data subject's responsibility, not the application's. Validation now
rejects any student record without a RUT with a clear error message; the
codec relies on RUT as the primary identifier and refuses to encode
records without it.

## Pseudonymization caveats

Strict legal pseudonymization (Ley 21.719 art. 4 lit. n; GDPR art. 4(5))
requires the additional information needed to re-identify subjects to be
"kept separately and subject to technical and organizational measures".
The UID key in this implementation is embedded in the application binary
and travels with it to the recording machine. That is, the UID key is
**not** kept separately from the data: a determined attacker who obtains
the binary can decrypt reversible course and legacy UIDs, and can test
candidate RUT values against `R1-` recording identifiers.

This is a deliberate trade-off for the LecturIA deployment context:

- **Offline operation is mandatory**. Many participating schools do not
  have reliable internet access, and a key delivery service that
  required connectivity at every session start would be unusable in
  exactly those locations the project most needs to support.
- **One-off out-of-band key distribution per teacher** would shift the
  problem rather than solve it: the key would still live on the
  teacher's machine after first use, with the same legal status.
- **Owner-side preprocessing** (the legally cleanest alternative, where
  teachers receive a pre-pseudonymized course package and never see
  RUTs or names in the app) breaks the fundamental UX requirement that
  teachers identify their students by name during recording sessions.

The chosen design therefore offers **strong de-identification of file
system outputs** rather than strict legal pseudonymization. Concretely:

- Casual disclosure (screenshots, file listings, accidental backups,
  cloud sync) does not expose RUT or name. This covers the high-frequency
  privacy risks in this deployment.
- A passive attacker who only obtains `.lra` files cannot recover any
  PII from the file names.
- An active attacker who additionally obtains the application binary can
  extract the UID key. That key decrypts reversible course and legacy UIDs;
  `R1-` recording identifiers remain one-way but can be tested against
  candidate RUT values.
- A targeted attacker who has both the binary and a Chilean roster can
  match candidate RUTs to `R1-` identifiers quickly. This is prevented for
  casual use but is not a defensible posture against a motivated adversary.

The decision is documented here so that a future ethics committee, audit,
or migration to a stricter pseudonymization model has the full context.

## UID key custody

The UID key is generated by `LecturIA.GenerateKeys` alongside the RSA
key pair and written to `src/LecturIA.App/Assets/uid-key.txt` (single-line
base64). The repository `.gitignore` blocks the file from being committed
under any name. CI builds materialize the file from the `UID_KEY_B64`
GitHub Actions secret (the same base64 content as the local file) so the
generated binary is self-contained. Both `ci.yml` and `release.yml` write
this secret to `src/LecturIA.App/Assets/uid-key.txt` before building.

The owner keeps a copy of the UID key alongside the RSA private key, in
the same custody (password manager, encrypted offline backup). Linked
tools that need to decode UIDs use that copy.

A rotation of the UID key invalidates every identifier produced under the
old key: courses persisted with old reversible UIDs become undecodable, and
both legacy and `R1-` recording file names can no longer be matched to roster
RUTs. Rotation requires re-importing every roster and an explicit migration
of recording identifiers if completion history must be preserved.
