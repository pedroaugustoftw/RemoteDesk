# Testes cliente Windows

Execute com SDK .NET 8 em uma sessão Windows interativa:

```powershell
E:\AnyCraft\.tools\dotnet\dotnet.exe run --project tests/client/RemoteDesk.ClientTests.csproj
```

Os testes capturam a tela atual para validar JPEG em memória e gravam arquivos aleatórios na pasta `bin/Debug/net8.0-windows/artifacts`. Não gravam imagens de tela e não injetam entrada de mouse ou teclado. Não abrem diálogos. O relatório completo fica em `artifacts/results.txt`.

Para incluir autenticação, autorização, retransmissão de imagem e arquivo reais:

```powershell
$env:REMOTEDESK_TEST_SERVER = 'wss://SEU-IP:5081/ws'
$env:REMOTEDESK_TEST_SERVER_PIN = 'SHA256-DO-CERTIFICADO-64-HEX'
$env:REMOTEDESK_TEST_RELAY = '1'
E:\AnyCraft\.tools\dotnet\dotnet.exe run --project tests/client/RemoteDesk.ClientTests.csproj
```

O servidor precisa estar configurado com relay independente. O teste usa duas identidades RSA temporárias, aceita explicitamente visualização e arquivos, sem controle, e encerra a sessão. Para esses testes, o relay deve usar o mesmo certificado do servidor. Também verifica falha de handshake com pin incorreto. Sem `REMOTEDESK_TEST_SERVER`, os testes de rede não são executados.

Isso valida duas instâncias no mesmo computador e a infraestrutura local TLS. Não substitui a validação em dois computadores, NAT ou VPS pela internet.
