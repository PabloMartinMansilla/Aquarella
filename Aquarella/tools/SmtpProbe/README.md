# Prueba independiente de SMTP

Preparada el 2026-10-09. **No se ejecutó ningún envío real.**

## Alcance y selección de transporte

Development utiliza el simulador si `Email:Smtp:Enabled=false` (o falta); utiliza
`SmtpEmailSender` si es true. Configuración SMTP incompleta no produce fallback
silencioso al simulador: el transporte queda no disponible. Production conserva
SMTP como transporte, deshabilitado/no disponible si no está configurado.

La selección está centralizada en `AccountEmailTransport.Register`, invocado por
Program. No era posible activar SMTP Development solamente mediante variables,
porque Program registraba incondicionalmente el simulador. No se cambió la lógica
SMTP, TLS, cola, cuentas ni persistencia.

SmtpProbe reutiliza `SmtpEmailSender`; no crea host/Blazor/worker, no registra EF,
no utiliza SQLite y no necesita `Accounts:PublicOrigin`. No agrega rutas web.
Exige `DOTNET_ENVIRONMENT=Development`; si ASPNETCORE_ENVIRONMENT existe también
debe ser Development. Sin entorno, Production, Staging o entornos contradictorios
se rechazan antes de cargar secretos.

Solo `--send --to <un-correo>` puede cargar la configuración y enviar. Sin argumentos
o con `--help` muestra ayuda sin leer secretos. No acepta contraseñas por argumentos,
destinatarios múltiples, contenido personalizado ni opciones de reintento. Envía
un mensaje fijo sin datos reales ni enlaces de cuenta. Hace una sola llamada al
transporte y termina; incluso ante un fallo no reintenta.

## Configuración privada

La herramienta utiliza el UserSecretsId del assembly Aquarella existente:
`Aquarella-local-development`. Lee exclusivamente esa fuente de configuración,
sin appsettings, overrides de variables SMTP ni configuración de Production.
Esto evita heredar accidentalmente un servidor/destinatario de otra configuración.
El proveedor de User Secrets lee su archivo privado al ejecutar la prueba autorizada;
no se ejecutó esa carga durante el desarrollo/verificación de la herramienta.

Ejecutar vos en PowerShell privado, fuera de la terminal compartida de Codex:
Los ejemplos parten de la raíz del repositorio. El correo real se introduce
interactivamente y se guarda en User Secrets; no se incorpora a appsettings.json.
Si ya está configurado allí, no es necesario volver a introducirlo.

```powershell
$taskProject = Join-Path (Get-Location).Path 'Aquarella\Aquarella\Aquarella.csproj'
$taskProbe = Join-Path (Get-Location).Path 'Aquarella\tools\SmtpProbe\SmtpProbe.csproj'
$taskSender = Read-Host 'Cuenta Gmail que utilizarás como remitente'

dotnet user-secrets set 'Email:Smtp:Enabled' 'false' --project $taskProject
dotnet user-secrets set 'Email:Smtp:Host' 'smtp.gmail.com' --project $taskProject
dotnet user-secrets set 'Email:Smtp:Port' '587' --project $taskProject
dotnet user-secrets set 'Email:Smtp:Security' 'StartTls' --project $taskProject
dotnet user-secrets set 'Email:Smtp:Username' $taskSender --project $taskProject
dotnet user-secrets set 'Email:Smtp:FromAddress' $taskSender --project $taskProject
dotnet user-secrets set 'Email:Smtp:FromName' 'Aquarella' --project $taskProject
dotnet user-secrets set 'Email:Smtp:TimeoutSeconds' '20' --project $taskProject
```

No hace falta `user-secrets init`. User Secrets está fuera del repositorio, pero no
es un almacén cifrado; proteger la cuenta de Windows y no publicar su archivo.

`Email:Smtp:FromAddress` queda vacío en la configuración compartida. La aplicación
en Development utiliza el valor privado de User Secrets con mayor prioridad;
SmtpProbe ya utiliza exclusivamente User Secrets. Por eso deben estar configurados
allí tanto Username como FromAddress, además de la contraseña de aplicación.
Sin un remitente válido no se habilita SMTP; no se inventa una dirección alternativa.
Production sigue con SMTP deshabilitado y, si se configura posteriormente, requiere
su propio FromAddress mediante configuración privada del entorno.

Introducir la contraseña sin escribirla en los argumentos ni mostrarla:

```powershell
$taskPassword = Read-Host 'Contraseña de aplicación de Google' -AsSecureString
$taskPointer = [IntPtr]::Zero
try {
    $taskPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskPassword)
    @{ 'Email:Smtp:Password' = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer) } |
        ConvertTo-Json -Compress | dotnet user-secrets set --project $taskProject
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar la contraseña.' }
}
finally {
    if ($taskPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer)
    }
    $taskPassword.Dispose()
    Remove-Variable taskPassword, taskPointer -ErrorAction SilentlyContinue
}
```

No ejecutar `user-secrets list` ni compartir esa terminal, screenshots, archivo o
contraseña. La conversión a texto existe brevemente en memoria para entregar JSON
al CLI por stdin; no escribe un archivo intermedio ni la imprime en consola.

## Un único correo real — solo cuando decidas ejecutarlo

Cerrar otras instancias de Aquarella en Development antes de activar SMTP para evitar
que alguna lea Enabled=true y procese su cola. La herramienta no las inicia ni cierra.
El cambio de Enabled es compartido con la aplicación y no está limitado al ejecutable
de prueba. Esta tarea no inició ni modificó ninguna instancia/base real.

En la misma terminal privada, luego de revisar y autorizar:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$taskRecipient = Read-Host 'Correo de prueba que controlás'

dotnet user-secrets set 'Email:Smtp:Enabled' 'true' --project $taskProject
if ($LASTEXITCODE -ne 0) { throw 'No se pudo habilitar SMTP.' }
try {
    dotnet run --project $taskProbe --no-launch-profile -- --send --to $taskRecipient
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'La prueba no terminó correctamente. Revisá la bandeja antes de intentar nuevamente.'
    }
}
finally {
    dotnet user-secrets set 'Email:Smtp:Enabled' 'false' --project $taskProject
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Restablecé manualmente Email:Smtp:Enabled=false antes de iniciar Aquarella.' }
}
```

El comando inicia únicamente el ejecutable de prueba. El build de su referencia
Aquarella no ejecuta Program ni migraciones. El envío puede ser aceptado por SMTP
sin llegar a la bandeja principal; comprobar recepción y spam. Un fallo puede ocurrir
después de que el servidor haya aceptado el mensaje: no se reintenta automáticamente.

Esto no verifica el flujo de registro/recuperación ni sus tokens. Esas pruebas deben
hacerse después sobre una instancia y base ficticias explícitamente aisladas, nunca
habilitando correo contra la base real durante esta prueba de transporte.

Para volver al simulador en cualquier momento:

```powershell
dotnet user-secrets set 'Email:Smtp:Enabled' 'false' --project $taskProject
```

Reiniciar la aplicación cuando corresponda para que se registre el transporte elegido;
no cambia de transporte en caliente en una instancia ya iniciada. Los User Secrets
locales no activan SMTP Production. No configurar Enabled=true en appsettings compartido.

## Validación sin secretos, red ni SQLite

Desde la carpeta Aquarella:

```powershell
dotnet build tools/SmtpProbeVerification --artifacts-path "$env:TEMP\aquarella-smtp-probe-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet "$env:TEMP\aquarella-smtp-probe-build\bin\SmtpProbeVerification\debug\SmtpProbeVerification.dll"
dotnet build Aquarella.slnx --artifacts-path "$env:TEMP\aquarella-smtp-probe-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
```

Resultado: 31 comprobaciones correctas y builds de app/herramienta/verificador con
0 errores y 0 warnings. Configuración ficticia in-memory y emisor falso; ninguna
llamada a Gmail, ningún User Secret leído, ninguna SQLite abierta/migrada ni app
iniciada. Se verificó además la CLI real con --help y rechazo en Production.
No se ejecutaron verificadores anteriores que migran fixtures o inician Aquarella,
para respetar los límites de esta tarea. NuGet usó únicamente el caché local.

Archivos de esta tarea: Program.cs de la aplicación, AccountEmail.cs (selección),
tools/SmtpProbe (csproj, Program, runner, este README) y tools/SmtpProbeVerification
(csproj y Program). No hay nuevas dependencias externas ni cambios de autenticación,
migraciones, base, puertos, rutas, Docker o Railway. Todos los cambios anteriores
se conservaron. Sin staging, commit ni push; rama main.
