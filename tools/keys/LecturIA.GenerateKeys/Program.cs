using System.Security.Cryptography;
using System.Text;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace LecturIA.GenerateKeys;

/// <summary>
/// Standalone tool that mints a fresh RSA-4096 key pair for LecturIA
/// recording encryption.
/// </summary>
/// <remarks>
/// The public key is meant to be committed to the repository at
/// <c>src/LecturIA.App/Assets/public-key.pem</c> so the application embeds
/// it in the binary. The private key is the single root of trust for
/// decrypting recordings: lose it and all recorded audio becomes
/// permanently unrecoverable; leak it and the encryption is fully
/// bypassed. The default destination for the private key is therefore a
/// path outside the repository, and the operator is expected to relocate
/// it to a password manager or encrypted offline backup immediately after
/// generation.
/// </remarks>
internal static class Program
{
    private const int KeySizeBits = 4096;
    private const int FingerprintByteLength = 8;
    private const int UidKeyLengthBytes = 32;

    /// <summary>
    /// Payload signed to mint an admin authentication token. Must stay
    /// in sync with <c>LecturIA.Core/Crypto/AdminTokenPayload.cs</c>.
    /// </summary>
    private const string AdminTokenPayload = "LecturIA.Admin.v1";

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options is null)
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            if (options.AdminKeygenMode)
            {
                return GenerateAdminKeyPair(options);
            }

            if (options.AdminTokenMode)
            {
                return MintAdminToken(options);
            }

            var rsaGenerated = MaybeGenerateRsa(options);
            var uidGenerated = MaybeGenerateUidKey(options);

            if (!rsaGenerated && !uidGenerated)
            {
                Console.WriteLine("Nothing to do: every output file already exists. Use --force to overwrite.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("ACTION REQUIRED");
            Console.WriteLine("  1. Move the private key to a password manager or encrypted");
            Console.WriteLine("     offline backup. Do not leave it on a regularly used machine.");
            Console.WriteLine("  2. Delete the local copy of the private key after you have stored");
            Console.WriteLine("     it safely.");
            Console.WriteLine("  3. The UID key file is consumed at build time and embedded into");
            Console.WriteLine("     the application binary. Keep a copy alongside the private key");
            Console.WriteLine("     so linked tools can decode UIDs.");
            Console.WriteLine("  4. The repository .gitignore blocks both the UID key and any");
            Console.WriteLine("     non-public PEM file. Only commit the public key.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int GenerateAdminKeyPair(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminKeyPath))
        {
            throw new ArgumentException("--admin-key is required when generating an admin key pair.");
        }

        EnsureFreshDestination(options.AdminKeyPath, options.Force, "admin private key");
        EnsureParentDirectory(options.AdminKeyPath);

        Console.WriteLine("Generating Ed25519 admin key pair...");

        var random = new SecureRandom();
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(random));
        var pair = generator.GenerateKeyPair();

        var privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        var publicKey = (Ed25519PublicKeyParameters)pair.Public;

        var privateBase64 = Convert.ToBase64String(privateKey.GetEncoded());
        var publicBase64 = Convert.ToBase64String(publicKey.GetEncoded());

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(options.AdminKeyPath, privateBase64 + Environment.NewLine, encoding);

        var label = string.IsNullOrWhiteSpace(options.AdminLabel)
            ? string.Empty
            : $"  # {options.AdminLabel.Trim()}";

        Console.WriteLine($"Admin private key : {options.AdminKeyPath}");
        Console.WriteLine();
        Console.WriteLine("Append this line to src/LecturIA.App/Assets/admin-public-keys.txt:");
        Console.WriteLine();
        Console.WriteLine($"{publicBase64}{label}");
        Console.WriteLine();
        Console.WriteLine("ACTION REQUIRED");
        Console.WriteLine("  1. Store the admin private key file in a password manager or");
        Console.WriteLine("     encrypted offline backup. It is the distributor's credential.");
        Console.WriteLine("  2. Delete the local copy after storing it safely. The .gitignore");
        Console.WriteLine("     blocks *.key files, but do not rely on that as the only guard.");
        Console.WriteLine("  3. Commit the updated admin-public-keys.txt and ship a new build");
        Console.WriteLine("     so the application trusts this distributor.");
        return 0;
    }

    private static int MintAdminToken(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminKeyPath))
        {
            throw new ArgumentException("--admin-key is required when minting an admin token.");
        }

        if (!File.Exists(options.AdminKeyPath))
        {
            throw new FileNotFoundException(
                $"Admin private key file not found: '{options.AdminKeyPath}'.",
                options.AdminKeyPath);
        }

        var privateBase64 = File.ReadAllText(options.AdminKeyPath).Trim();
        byte[] privateBytes;
        try
        {
            privateBytes = Convert.FromBase64String(privateBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                $"Admin private key file '{options.AdminKeyPath}' does not contain valid base64.", ex);
        }

        var privateKey = new Ed25519PrivateKeyParameters(privateBytes, 0);

        var payload = Encoding.UTF8.GetBytes(AdminTokenPayload);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();

        // Write the token to stdout only, no decoration, so it is easy to
        // pipe into a password manager: `... --admin-token | clip`.
        Console.WriteLine(Convert.ToBase64String(signature));
        return 0;
    }

    private static bool MaybeGenerateRsa(Options options)
    {
        var publicExists = File.Exists(options.PublicKeyPath);

        if (publicExists && !options.Force)
        {
            Console.WriteLine("Public key already exists, skipping RSA generation. Use --force to rotate the pair.");
            return false;
        }

        EnsureParentDirectory(options.PublicKeyPath);
        EnsureParentDirectory(options.PrivateKeyPath);

        Console.WriteLine($"Generating RSA-{KeySizeBits} key pair...");
        using var rsa = RSA.Create(KeySizeBits);

        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();

        var spkiDer = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(spkiDer);
        var fingerprint = Convert.ToHexString(hash.AsSpan(0, FingerprintByteLength))
            .ToLowerInvariant();

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(options.PublicKeyPath, publicPem, encoding);
        File.WriteAllText(options.PrivateKeyPath, privatePem, encoding);

        Console.WriteLine($"Public key  : {options.PublicKeyPath}");
        Console.WriteLine($"Private key : {options.PrivateKeyPath}");
        Console.WriteLine($"Fingerprint : {fingerprint}");
        return true;
    }

    private static bool MaybeGenerateUidKey(Options options)
    {
        if (string.IsNullOrEmpty(options.UidKeyPath))
        {
            return false;
        }

        if (File.Exists(options.UidKeyPath) && !options.Force)
        {
            Console.WriteLine("UID key already exists, skipping. Use --force to rotate the key.");
            return false;
        }

        EnsureParentDirectory(options.UidKeyPath);

        Console.WriteLine($"Generating {UidKeyLengthBytes * 8}-bit UID key...");
        Span<byte> material = stackalloc byte[UidKeyLengthBytes];
        try
        {
            RandomNumberGenerator.Fill(material);
            var base64 = Convert.ToBase64String(material);

            File.WriteAllText(
                options.UidKeyPath,
                base64 + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var fingerprint = Convert.ToHexString(
                SHA256.HashData(material).AsSpan(0, FingerprintByteLength))
                .ToLowerInvariant();

            Console.WriteLine($"UID key     : {options.UidKeyPath}");
            Console.WriteLine($"UID key FP  : {fingerprint}");
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static Options? ParseArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        string? publicKeyPath = null;
        string? privateKeyPath = null;
        string? uidKeyPath = null;
        string? adminKeyPath = null;
        string? adminLabel = null;
        var force = false;
        var adminTokenMode = false;
        var adminKeygenMode = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--public-key":
                case "-p":
                    publicKeyPath = ReadValue(args, ref i);
                    break;
                case "--private-key":
                case "-s":
                    privateKeyPath = ReadValue(args, ref i);
                    break;
                case "--uid-key":
                case "-u":
                    uidKeyPath = ReadValue(args, ref i);
                    break;
                case "--admin-key":
                case "-k":
                    adminKeyPath = ReadValue(args, ref i);
                    break;
                case "--admin-label":
                case "-l":
                    adminLabel = ReadValue(args, ref i);
                    break;
                case "--admin-keygen":
                case "-g":
                    adminKeygenMode = true;
                    break;
                case "--admin-token":
                case "-t":
                    adminTokenMode = true;
                    break;
                case "--force":
                case "-f":
                    force = true;
                    break;
                case "--help":
                case "-h":
                    return null;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (adminKeygenMode && adminTokenMode)
        {
            throw new ArgumentException("--admin-keygen and --admin-token are mutually exclusive.");
        }

        if (adminKeygenMode)
        {
            if (string.IsNullOrWhiteSpace(adminKeyPath))
            {
                throw new ArgumentException("--admin-key is required when generating an admin key pair.");
            }

            return new Options(
                PublicKeyPath: string.Empty,
                PrivateKeyPath: string.Empty,
                UidKeyPath: null,
                Force: force,
                AdminTokenMode: false,
                AdminKeygenMode: true,
                AdminKeyPath: Path.GetFullPath(adminKeyPath),
                AdminLabel: adminLabel);
        }

        if (adminTokenMode)
        {
            if (string.IsNullOrWhiteSpace(adminKeyPath))
            {
                throw new ArgumentException("--admin-key is required when minting an admin token.");
            }

            return new Options(
                PublicKeyPath: string.Empty,
                PrivateKeyPath: string.Empty,
                UidKeyPath: null,
                Force: false,
                AdminTokenMode: true,
                AdminKeygenMode: false,
                AdminKeyPath: Path.GetFullPath(adminKeyPath),
                AdminLabel: null);
        }

        if (string.IsNullOrWhiteSpace(publicKeyPath))
        {
            throw new ArgumentException("--public-key is required.");
        }

        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            throw new ArgumentException("--private-key is required.");
        }

        return new Options(
            Path.GetFullPath(publicKeyPath),
            Path.GetFullPath(privateKeyPath),
            string.IsNullOrWhiteSpace(uidKeyPath) ? null : Path.GetFullPath(uidKeyPath),
            force,
            AdminTokenMode: false,
            AdminKeygenMode: false,
            AdminKeyPath: null,
            AdminLabel: null);
    }

    private static string ReadValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for argument '{args[i]}'.");
        }

        i++;
        return args[i];
    }

    private static void EnsureFreshDestination(string path, bool force, string label)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (!force)
        {
            throw new InvalidOperationException(
                $"A {label} already exists at '{path}'. Re-run with --force to overwrite.");
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("LecturIA.GenerateKeys");
        Console.WriteLine("Generates RSA-4096 key material for LecturIA and mints distributor tokens.");
        Console.WriteLine();
        Console.WriteLine("Key generation:");
        Console.WriteLine("  dotnet run --project tools/keys/LecturIA.GenerateKeys -- \\");
        Console.WriteLine("    --public-key  <path-to-public.pem> \\");
        Console.WriteLine("    --private-key <path-to-private.pem> \\");
        Console.WriteLine("    [--uid-key    <path-to-uid-key.txt>] \\");
        Console.WriteLine("    [--force]");
        Console.WriteLine();
        Console.WriteLine("Admin key pair generation (Ed25519, one per distributor):");
        Console.WriteLine("  dotnet run --project tools/keys/LecturIA.GenerateKeys -- \\");
        Console.WriteLine("    --admin-keygen \\");
        Console.WriteLine("    --admin-key   <path-to-new-private.key> \\");
        Console.WriteLine("    [--admin-label \"Distributor name\"]");
        Console.WriteLine();
        Console.WriteLine("Admin token minting (distributor signs the login payload):");
        Console.WriteLine("  dotnet run --project tools/keys/LecturIA.GenerateKeys -- \\");
        Console.WriteLine("    --admin-token \\");
        Console.WriteLine("    --admin-key <path-to-private.key>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --public-key   Destination for the RSA public key (committed to repo).");
        Console.WriteLine("  -s, --private-key  Destination for the RSA private key (must NOT be committed).");
        Console.WriteLine("  -u, --uid-key      Destination for the UID key (must NOT be committed).");
        Console.WriteLine("                     Required only when generating or rotating the UID key.");
        Console.WriteLine("  -g, --admin-keygen Generate a new Ed25519 admin key pair. Writes the");
        Console.WriteLine("                     private key to --admin-key and prints the public key");
        Console.WriteLine("                     line to append to admin-public-keys.txt.");
        Console.WriteLine("  -t, --admin-token  Sign the well-known admin payload with the Ed25519");
        Console.WriteLine("                     private key at --admin-key and write the base64 token.");
        Console.WriteLine("  -k, --admin-key    Path to the Ed25519 admin private key. Destination in");
        Console.WriteLine("                     keygen mode, source in token mode.");
        Console.WriteLine("  -l, --admin-label  Optional label appended as a comment to the public");
        Console.WriteLine("                     key line in keygen mode.");
        Console.WriteLine("  -f, --force        Overwrite existing files at the destinations.");
        Console.WriteLine("  -h, --help         Show this message.");
        Console.WriteLine();
        Console.WriteLine("The tool is idempotent: if a destination file already exists and --force");
        Console.WriteLine("is not given, that file is left alone. Run with --uid-key only to add the");
        Console.WriteLine("UID key alongside an already-generated RSA key pair. The RSA key pair");
        Console.WriteLine("protects recordings; the Ed25519 admin keys authenticate distributors and");
        Console.WriteLine("are entirely separate.");
    }

    private sealed record Options(
        string PublicKeyPath,
        string PrivateKeyPath,
        string? UidKeyPath,
        bool Force,
        bool AdminTokenMode,
        bool AdminKeygenMode,
        string? AdminKeyPath,
        string? AdminLabel);
}
