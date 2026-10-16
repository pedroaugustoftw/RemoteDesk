# Instalador servidor e serviço Windows

Arquivo: `artifacts/win-x64/server-installer/RemoteDesk-Server-Setup.exe`.

Execute-o no PC servidor. A janela mostra endereços e impressões SHA-256 dos certificados, com botão para copiar. O endereço a configurar no cliente é o de sinalização, porta **5081**. A porta **5091** pertence ao relay e é anunciada automaticamente.

Antes de instalar, encerre a infraestrutura manual: `scripts/Stop-Lan.ps1`. Clique **Instalar e iniciar**. O Windows solicitará elevação de administrador para registrar o serviço; confirme manualmente. O serviço se chama **RemoteDeskServer**, nome exibido **RemoteDesk Server**, inicia com o Windows e roda como LocalService sem janelas de console. Ele fica visível em Serviços e nos processos do Windows. A janela do instalador tem Iniciar, Parar e Remover serviço.

Somente sinalização e retransmissão são executadas pelo serviço. Ele não captura tela, injeta entrada nem oferece acesso não supervisionado. Essas funções continuam no cliente interativo com consentimento explícito.

Os componentes ficam em `%ProgramFiles%\RemoteDesk Server`. Configurações, registro de IDs e logs ficam em `%ProgramData%\RemoteDeskServer`. O certificado TLS possui chave privada no PFX cifrado; senha e segredo interno do relay são aleatórios e protegidos por DPAPI LocalMachine em uma pasta privada com ACL para Administrators, SYSTEM e LocalService. As impressões exibidas são públicas, não senhas ou chaves privadas.

Se houver uma configuração LAN válida no perfil do usuário instalador com o mesmo IP, o instalador migra certificado e registro de dispositivos, preservando as impressões configuradas. Caso contrário, gera um certificado próprio e mostra suas novas impressões. Clientes em outros PCs precisam receber o endereço e ambas as impressões por um canal confiável.

Não adiciona regras de firewall nem altera a confiança global de certificados. TCP 5081/5091 precisa estar acessível na LAN; a API privada do relay permanece em 127.0.0.1:5092.

Publicação: `scripts/publish-server-service.ps1`. O servidor, relay e supervisor incluem runtime, dispensando instalar .NET no PC de destino.

## Validação e limites

Compilação, leitura do payload, geração/recarga de PFX e DPAPI, integridade das impressões e preservação da configuração são verificáveis sem administrador. O terminal de desenvolvimento não é administrador; o registro e funcionamento sob o Service Control Manager exigem validação elevada no PC de destino. Nenhum serviço foi instalado automaticamente durante a criação do instalador.
