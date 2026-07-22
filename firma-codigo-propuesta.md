# Firma de código para LecturIA: opciones y costos

Documento de propuesta para evaluación de financiamiento. No forma parte
de la documentación técnica del repositorio.

## Por qué se necesita

LecturIA se distribuye como ejecutable Windows (`.exe`) a profesores. Sin
firma digital, Windows muestra el aviso de SmartScreen "aplicación no
reconocida" en cada ejecución. Esto:

- Reduce la credibilidad de la herramienta frente a docentes.
- Obliga al usuario a entrar en "Más información > Ejecutar de todos modos"
  cada vez que se descarga una nueva versión.
- Puede bloquear la ejecución en equipos institucionales con políticas
  restrictivas (Defender for Endpoint, Intune, etc.).

Una firma vincula el `.exe` a una identidad verificada por una Certificate
Authority (CA) y construye reputación SmartScreen con el tiempo. Para un
estudio que depende de la cooperación voluntaria de profesores, eliminar
esta fricción protege la validez de los datos recolectados.

## Restricciones del mercado actual (2026)

- Desde **junio 2023**, todos los certificados de firma requieren
  almacenamiento en hardware (HSM o token USB FIPS 140-2 L2+) por mandato
  del CA/Browser Forum. Ya no existen certificados software-only.
- Desde **febrero 2026**, la validez máxima bajó de 3 años a **1 año**.
  Cualquier opción es recurrente, no compra única.
- [Azure Trusted Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options),
  el servicio cloud de Microsoft equivalente a las opciones siguientes,
  **no acepta a Chile** ni como organización ni como individuo.

## Opciones evaluadas

Ordenadas por costo, todas viables desde Chile.

### 1. SignPath Foundation - USD 0/año (preferida)

[SignPath Foundation](https://signpath.org/) emite certificados gratuitos a
proyectos open source con licencia OSI. LecturIA cumple: MIT, copyright
FCFM Universidad de Chile, build automatizado en GitHub Actions.

- Costo: USD 0/año.
- Tiempo de aprobación: 2 a 6 semanas, sujeto a revisión manual.
- Riesgo: aprobación no garantizada (criterio de la fundación).
- Aplicación: https://signpath.org/apply

### 2. SSL.com eSigner - USD ~250/año

Cloud signing nativo. Sin token físico, sin shipping. Firma vía API desde
GitHub Actions.

- Costo año 1: USD 199 a 299 según plan.
- Costo años siguientes: igual.
- Producto: https://www.ssl.com/esigner/

### 3. Sectigo o Comodo IV en Azure Key Vault - USD ~245/año

Certificado Individual Validation (IV) emitido a un investigador chileno
(documento de identidad + selfie + verificación). Se compra con la opción
"Install on existing HSM" y se provisiona en Azure Key Vault, evitando
token físico y shipping internacional recurrente (~USD 130/año adicionales).

- Costo año 1: ~USD 220 (certificado) + ~USD 25 (Azure Key Vault) = **~USD 245**.
- Costo años siguientes: ~USD 245.
- Resellers viables:
  - https://signmycode.com/sectigo-individual-code-signing (USD 219.99)
  - https://certera.com/code-signing/certera-code-signing (USD 215.99)
  - https://sectigostore.com/code-signing/sectigo-code-signing-certificate

### 4. Sectigo o Comodo IV con token físico - USD ~350/año

Mismo certificado que la opción 3 pero con token USB enviado por courier
internacional.

- Costo año 1: ~USD 220 (cert) + ~USD 130 (shipping) = **~USD 350**.
- Costo años siguientes: ~USD 350 (token nuevo cada renovación desde 2026).
- Requiere el token físicamente conectado al equipo que firma (incompatible
  con CI desatendido).
- **No recomendado** salvo que no haya acceso a Azure.

## Lo que se descartó

- **Self-signed certificate**: SmartScreen lo trata exactamente igual que
  sin firma. Cero beneficio.
- **Certificado a nombre de un tercero en USA, Canadá, UE o UK**: viola los
  términos de la CA y compromete la identidad ajena.
- **Azure Trusted Signing**: no disponible para Chile.

## Recomendación

1. **Aplicar a SignPath Foundation** (opción 1, USD 0). Avanzar en paralelo
   con el resto del despliegue mientras se procesa la solicitud.
2. **Si SignPath se rechaza o demora más de un mes**: contratar SSL.com
   eSigner (opción 2) o Sectigo IV en Azure Key Vault (opción 3),
   aproximadamente **USD 250/año**.
3. La opción 4 (token físico) sólo si no hay acceso a Azure.

## Resumen económico

| Escenario                          | Costo año 1 | Costo años 2+ |
|------------------------------------|-------------|---------------|
| SignPath Foundation aprueba        | USD 0       | USD 0         |
| SSL.com eSigner                    | USD ~250    | USD ~250      |
| Sectigo IV + Azure Key Vault       | USD ~245    | USD ~245      |
| Sectigo IV + token USB             | USD ~350    | USD ~350      |

Una vez emitido, el certificado firma versiones ilimitadas de LecturIA
durante el período de validez sin costo adicional, y la reputación
SmartScreen es acumulativa: cada release construye sobre la anterior.
