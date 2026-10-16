Prévia 1.1 do RemoteDesk.

- Instaladores Windows cliente/servidor com registro em Aplicativos instalados e desinstalador próprio.
- Cliente desktop Qt para Windows x64, Linux x64 e macOS Intel/Apple Silicon, usando o broker/relay RemoteDesk existente.
- Captura JPEG, visualização, consentimento explícito, permissões separadas, controle, qualidade ajustável e arquivos com SHA-256.
- Caminhos POSIX corrigidos no explorador remoto do cliente Windows.
- Testes automatizados de autenticação, autorização e arquivos, integração real com broker/relay .NET e abertura dos pacotes no sistema de build.

**Prévia experimental:** as compilações nativas e os testes automatizados não homologam sessões entre máquinas físicas das três plataformas. Linux host exige X11; Wayland é somente visualizador/arquivos. macOS exige permissões de Gravação de Tela e Acessibilidade; pacotes ad-hoc, sem notarização. Qt não oferece recebimento não supervisionado, áudio, multimonitor ou todos os recursos/visual do cliente Windows.

Configure todos os clientes com o mesmo servidor. Consulte [configuração e limitações](https://github.com/pedroaugustoftw/RemoteDesk/blob/main/docs/cross-platform.md). No túnel informado: servidor `wss://remote.example.com/ws`, relay `wss://remote.example.com/relay/ws`, SHA TLS vazia para certificado público válido. O servidor precisa estar em modo VPS/gateway correspondente.

Verifique os arquivos usando `SHA256SUMS.txt`. Identidades/certificados pessoais e dados de produção não fazem parte dos pacotes.
