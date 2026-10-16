# Descoberta autenticada — 15/10/2026

Build `server/RemoteDesk.Server.csproj`: sucesso, zero avisos e erros.

Servidor isolado: `http://127.0.0.1:5390`, sem relay configurado. Registro de teste: `tests/server/artifacts/discovery-devices.json`. Servidor encerrado após execução; infraestrutura LAN não foi alterada.

Comando: `.tools/dotnet/dotnet.exe run --project RemoteDesk/tests/server/RemoteDesk.ServerTests.csproj -- ws://127.0.0.1:5390/ws --discovery` (executar a partir de `E:/AnyCraft`).

11 verificações passaram: autenticação RSA de dois dispositivos; correlação RequestId; dispositivo remoto online incluído e solicitante excluído; lista dentro dos limites; descoberta sem autenticação recusada; dispositivo desconectado removido; três consultas por segundo aceitas e quarta limitada.

Regressão sem `--discovery`: 19 verificações passaram, incluindo autorização explícita, permissões, retransmissão de imagem/arquivo/entrada, limites de mensagens, recusa e encerramento.

Não foram conectados 201 dispositivos para exercitar o corte em 200: esse limite é implementado por `Take(200)` e a verificação de tamanho foi executada com dois dispositivos. Nenhuma varredura de rede foi realizada e nenhuma entrada foi injetada no Windows.
