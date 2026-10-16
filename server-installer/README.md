# RemoteDesk Server Setup

Interface WPF com janela nativa, conteúdo rolável, ações de serviço e campos públicos separados para endereço e SHA-256 de servidor/relay. Os campos de conexão são somente leitura, permitem seleção e têm botões individuais para copiar. Senhas, chaves privadas e segredo do relay não são lidos pela interface.

O estado exibido é consultado diretamente no Service Control Manager, somente leitura, a cada dois segundos. `Iniciando`, `Parando` e `Em execução` representam estados do serviço Windows, sem afirmar que endpoints de rede já estão saudáveis. Sem permissão de consulta, aparece `Consulta indisponível`.

Extração, preparação de certificado e comandos de serviço executam em tarefa de segundo plano, com ações desabilitadas enquanto a operação está em andamento. O fechamento é bloqueado durante uma operação para evitar interromper a instalação. A elevação de administrador continua explícita por UAC; o IP digitado é preservado ao reabrir.

Verificação sem instalar serviço:

```powershell
dotnet build server-installer/RemoteDesk.ServerSetup.csproj -c Release
dotnet server-installer/bin/Release/net8.0-windows/RemoteDesk-Server-Setup.dll --verify-payload
$env:REMOTEDESK_SETUP_PREVIEW = 'C:\Temp\RemoteDesk-Server-Setup.png'
dotnet server-installer/bin/Release/net8.0-windows/RemoteDesk-Server-Setup.dll --render
```

`--render-small` verifica a janela de 680 × 560. `--render-details` captura a área rolada até os campos de conexão. As dimensões são limitadas à área de trabalho disponível. Essas opções não instalam nem iniciam serviços.
