# RemoteDesk

Versão 1.1 em prévia: [downloads](https://github.com/pedroaugustoftw/RemoteDesk/releases) e [cliente Windows/Linux/macOS, configuração e limites](docs/cross-platform.md). O cliente Windows WPF permanece disponível. A prévia Qt usa o mesmo servidor, mas ainda exige validação entre computadores físicos.


Aplicação desktop Windows de acesso remoto, implementada em código próprio. O escopo de implantação atual é **rede local Windows**. Cada conexão exige aceite local ou acesso por senha previamente habilitado pelo proprietário.

## Usar na rede local

Alternativa com serviço Windows: use `artifacts/win-x64/server-installer/RemoteDesk-Server-Setup.exe`. Mostra endereços e SHA-256 e registra **RemoteDesk Server** mediante elevação de administrador. Encerre `Start-Lan.ps1` antes de instalar. Veja [docs/server-service.md](docs/server-service.md). O serviço executa somente servidor/relay, sem captura ou controle deste PC.

1. Em um PC da rede, execute `scripts/Setup-Lan.ps1 -Ip SEU-IP` e depois `scripts/Start-Lan.ps1`. É necessário runtime .NET 8 nesse PC.
2. Abra `artifacts/win-x64/client/RemoteDesk.exe` nos dois PCs, ou use `artifacts/win-x64/installer/RemoteDesk-Setup.exe`. O instalador pede o servidor principal e os fingerprints na etapa Conexão; em atualização carrega a configuração existente.
3. Em **Configurações → Conexão**, informe `wss://SEU-IP:5081/ws` e salve.
4. Em **Acesso e Permissões**, informe a impressão SHA-256 exibida pelo setup nos dois campos de certificado, servidor e relay. Confirme a impressão diretamente no PC da infraestrutura.
5. Copie o ID do destino, digite-o no campo superior do outro cliente e solicite a conexão. O destino precisa clicar **Aceitar** e escolher as permissões. Mouse/teclado e arquivos começam desmarcados.
6. Use **Encerrar**, ou feche a janela de sessão, para terminar. Uma desconexão exige nova solicitação e novo consentimento.

As portas TCP 5081/5091 precisam estar alcançáveis entre os PCs. O instalador servidor configura as regras de firewall; os scripts manuais não alteram firewall nem confiança de certificados e não registram serviços; o instalador servidor registra um serviço automático mediante elevação explícita. Detalhes de instalação, geração de certificado e encerramento da infraestrutura: [docs/install.md](docs/install.md).

## Componentes e código

- `client/`: WPF .NET 8, identidade RSA protegida por DPAPI, configurações e histórico; captura GDI/JPEG; controle por SendInput dentro de sessão autorizada; transferência em blocos com confirmação de destino e SHA-256.
- `server/`: ASP.NET Core; autenticação por desafio RSA-PSS, registro persistente, solicitações, permissões e encerramento; emissão e renovação da autorização do relay.
- `relay/`: processo ASP.NET Core independente; tickets de uso único, papéis separados, limites de mídia, heartbeat e lease revogável. Usa WSS, sem protocolo proprietário.
- `shared/`: mensagens e validação comum.
- `tests/`, `installer/tests/`: testes executáveis sem framework externo.
- `scripts/`: publicação e infraestrutura LAN no usuário atual.
- `installer/`: instalador próprio por usuário, com atualização em staging e preservação das configurações.

Para compilar, use SDK .NET 8: `dotnet build RemoteDesk.sln -c Release`. Para publicar executáveis com runtime incluído: `scripts/publish.ps1 -Dotnet CAMINHO-DO-DOTNET`. O SDK provisionado nesta máquina fica em `E:\AnyCraft\.tools\dotnet\dotnet.exe`.

## O que foi validado

Compilação de cliente, servidor, relay e instalador; renderização e inspeção visual das telas inicial e de configurações; autenticação de duas identidades; aceite/recusa, permissões e encerramento; transmissão de captura JPEG real e arquivo real por WSS com integridade; rejeição de certificado incorreto; revogação e expiração de autorização do relay; políticas e staging do instalador.

Os testes de sessão usaram duas identidades no **mesmo computador**. Ainda não foi validado o conjunto em dois PCs físicos, no Windows 10, com vários monitores físicos ou com injeção real de entrada. Instalação completa com atalhos numa máquina separada também está pendente. Veja [docs/tests.md](docs/tests.md) para evidência e limites.

## Limites atuais

A interface inicial, menu principal e estrutura de Configurações seguem as três imagens observadas. A fidelidade visual completa, diálogos originais e tela de sessão da referência não foram comprovados; o relatório [docs/investigation.md](docs/investigation.md) distingue observações e lacunas. Favoritos são persistentes. Encontrados consulta dispositivos autenticados online no mesmo servidor, sem varrer a rede; exige também o servidor atualizado desta versão. Convites persistentes, Wake-On-LAN e gravação estão indisponíveis.

A captura usa GDI e JPEG por quadro, com resolução, qualidade, FPS e monitor configuráveis. Não há WGC, H.264/AV1, codificação GPU, áudio, clipboard compartilhado ou serviço de controle. A seleção de monitor ocorre antes de iniciar a sessão. O envio limita tamanho, qualidade e taxa; o cliente mantém somente o frame mais recente aguardando apresentação.

TLS protege cliente↔servidor e cliente↔relay. O relay consegue acessar o conteúdo; criptografia ponta a ponta, conexão direta e ICE/STUN/TURN não estão implementados. Implantação pela internet e otimização em condições reais de WAN ficam para uma fase posterior. Os executáveis ainda não possuem assinatura Authenticode.

## Investigação

O MCP REA foi utilizado com Ghidra em análise estática não invasiva. Evidências, hashes, medidas de tela e limitações estão em [docs/investigation.md](docs/investigation.md). Nenhum recurso ou código proprietário do aplicativo analisado integra os binários RemoteDesk.

Para republicar apenas cliente e seu instalador sem substituir binários do servidor em uso: `scripts/publish-client.ps1`. Para o pacote servidor/relay como serviço: `scripts/publish-server-service.ps1`. O código-fonte pode ser empacotado com `scripts/package-source.ps1` (PowerShell, sem Python); dados de execução, PFX e DPAPI são excluídos.

Cadastro de dispositivos, status online e acesso opcional por senha: [guia de configuração e segurança](docs/password-access.md). Identidade visual aprovada: Faixa R, azul #1D6FF2; arquivos em branding/final.

Ajuste por barra deslizante e durante a sessão: [velocidade e qualidade](docs/transmission-quality.md).

Novo fluxo de conexão, ponteiro azul e modo administrador opcional com UAC local: [guia](docs/connection-flow.md).

Tela de sessão e configuração persistente de senha: [guia](docs/viewer-password.md).

Configuração LAN e VPS/domínio: consulte [docs/network-modes.md](docs/network-modes.md).

Explorador e transferência de arquivos: [docs/file-transfer.md](docs/file-transfer.md).

Instalação registrada e desinstalação no Windows: [docs/windows-installation.md](docs/windows-installation.md).
