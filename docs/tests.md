# Validação executada

Ambiente: Windows 11 x64, SDK .NET 8.0.426. Não houve alteração nos binários originais, em firewall ou na confiança global de certificados.

| Grupo | Resultado | Evidência |
|---|---|---|
| Sinalização e permissões | 19 verificações passaram | evidence/server-integration.txt |
| Relay independente | 12 verificações passaram | evidence/relay-integration.txt |
| Leases em tempo real | renovação >70s e expiração órfã ~60s passaram | evidence/relay-lease-integration.txt |
| Cliente | 21 testes passaram | evidence/client-integration.txt |
| Instalador | 27 testes de política/staging passaram | installer/tests, relatório de distribuição |
| TLS LAN | saúde server/relay, pin correto e pin incorreto | relatório de distribuição e cliente |
| Interface | tela inicial e configurações renderizadas e inspecionadas; correções de recurso de ícone e tema realizadas | tests/artifacts/preview |

O teste do cliente usou as classes reais Connection e RelayConnection, duas chaves RSA distintas, TLS no IP da interface LAN deste computador, autorização explícita feita pelo teste, captura JPEG real e transferência de arquivo real para diretório de teste. Verificou hashes recebidos e encerramento da sessão. Não automatizou aceite na interface normal do produto.

A validação do instalador cobre arquivos em sandbox, rejeição de caminhos maliciosos, preservação de configurações e leitura do pacote embedded. Não representa uma instalação completa com atalhos no computador do usuário.

## Reprodução

Compilação: `dotnet build RemoteDesk.sln -c Release`.

Cliente: `dotnet run --project tests/client/RemoteDesk.ClientTests.csproj`. Para incluir rede WSS, siga `tests/client/README.md`, configure endereço e fingerprint da sua infraestrutura LAN e habilite o teste de relay.

Servidor e relay: consulte `server/README.md` e os programas em `tests/server` e `tests/relay`. Instalador: `dotnet run --project installer/tests/RemoteDesk.InstallerTests.csproj -c Release`.

Cliente e instalador incluem runtime na publicação x64. Servidor/relay compilam sem avisos. O projeto híbrido WPF/WinForms do cliente apresenta WFAC010 sobre a configuração DPI no manifesto; a compilação termina com êxito. A captura GDI usa WinForms para enumerar monitores, e a interface usa WPF.

## Testes ainda necessários

- Dois PCs Windows físicos na mesma LAN: solicitação na UI, aceitação/recusa local, tela contínua e transferência nos dois sentidos.
- Mouse, cliques, rolagem, texto, modificadores e liberação de entrada em aplicações de teste. O código SendInput está implementado, mas **nenhuma injeção de entrada foi executada** nesta validação.
- Firewall e alcance real entre computadores; Wi-Fi instável, suspensão, bloqueio de sessão e quedas físicas de rede.
- Vários monitores físicos, diferentes DPI, alternância de resolução e Windows 10.
- Desempenho prolongado, consumo de CPU/GPU e pressão de memória.
- Instalação/atualização completa com atalhos em outra conta ou computador.
- VPS, internet, NAT e transporte direto, após disponibilização desse ambiente.

Esses itens permanecem pendentes. Não são classificados como recursos testados ou fases concluídas.

## Revisão de15/10/2026

- Instalador cliente:60 verificações passaram, incluindo validação da URL/TLS, fingerprints, atualização sem mudanças, preservação de campos desconhecidos, falha antes dos binários e gravação atômica. Evidência: `installer/tests/configuration-evidence.txt`.
- Descoberta autenticada:11 verificações passaram, mais19 de regressão do servidor, em processo isolado na porta5390. Evidência: `tests/server/DISCOVERY-RESULTS.md`. Lista somente dispositivos autenticados online no servidor próprio, sem varredura da LAN.
- Infraestrutura: seis verificações de certificado, DPAPI e migração passaram novamente. O registro e funcionamento elevado no SCM continuam pendentes; nenhum serviço foi instalado nesta validação.
- Instalador servidor: build e leitura integral do payload passaram; telas normal e pequena, início e fim da rolagem, foram renderizadas e inspecionadas. Evidências: `docs/evidence/server-setup-ui*.png`.

Nesta revisão também foram executados20 testes locais do cliente: captura JPEG real, coordenadas, transferência, cancelamento, integridade e pin inválido;20PASS/0FAIL, sem injeção de entrada. A rodada não ativou os testes opcionais de rede WSS. Publicação servidor/relay/supervisor self-contained e instalador servidor concluída; payload final validado sem instalar serviço. SHA256 do instalador servidor: `E03C9E6CE4CB02E39EAD596486BBCC98314E318EA8AA1A732699ABBB12900699`.

A interface revisada passou em teste isolado de menu com oito ações, aba contextual, favoritos persistentes e remoção, campos JSON desconhecidos preservados, busca, rolagem dos dois painéis, roda do mouse e acesso ao Salvar em900×480. Renders das três telas foram conferidos em `artifacts/ui-v2-1`, `ui-v2-2` e `ui-v2-3`. Os renders usam ID e estado de conexão fictícios apenas no perfil de teste; não são evidência de uma sessão remota real. A largura foi limitada à área de trabalho disponível; a imagem de referência completa tem1918pixels.

O mesmo teste UI passou também no `artifacts/win-x64/client/RemoteDesk.exe` publicado, com perfil isolado em `artifacts/published-ui-v2`. Incluiu atualização de Encontrados via handler real `devices` e tratamento de JSON inválido, além das verificações de navegação e rolagem. Esse teste de UI não abriu conexão com outro computador.

## Inicialização e firewall — revisão posterior de15/10

Builds cliente/instaladores passaram. Teste --tray-test em perfil isolado confirmou ícone visível, recolhimento ao minimizar, restauração e saída explícita. Não iniciou conexão nem alterou registro Run.60 testes de regressão do instalador passaram. O registro automático no login, reinício do Windows e aplicação elevada das regras COM de firewall ainda precisam de validação no destino. O terminal não é administrador; nenhuma regra ou serviço foi alterado por esses testes.

Teste de bandeja também passou no executável self-contained publicado com --minimized --tray-test. Instaladores finais passaram na leitura do payload; telas com opção de inicialização e botão firewall foram renderizadas e inspecionadas. O serviço existente foi apenas consultado (Em execução), sem ser reinstalado ou reiniciado. Nenhum reinício real do Windows foi realizado.

## Visual cinza/preto e rolagem — revisão de15/10

A pedido do usuário, a versão atual substitui os destaques vermelhos e cartões coloridos por tons de cinza e preto. Esta orientação posterior autoriza as alterações visuais sobre a referência anterior. Cabeçalho/ID/seleção e marca vetorial na janela usam cinza, com indicador de conexão ainda semântico. Cartões passam a quebrar em linhas; a navegação horizontal mostra barra quando necessário; o menu tem altura limitada à janela e rolagem própria. Checkboxes podem quebrar textos, diálogos têm altura limitada e rolagem, e a barra da sessão usa quebra de linha.

O roteamento da roda procura o painel sob o ponteiro que ainda pode rolar, em vez de consumir toda roda no painel externo. Shift+roda atua horizontalmente. Corrigidos comandos de avanço/recuo da barra horizontal. Teste em900×480 passou para painel de configurações, lateral, roda sobre cartões, página inicial, fim do menu, busca e Salvar. Regressões de favoritos, campos JSON futuros e resposta devices passaram. Renders de início/configurações/menu foram conferidos em `artifacts/gray-render*`. São dados fictícios apenas para apresentação. Permanece o aviso WFAC010 existente na compilação; nenhuma mudança de protocolo, permissões ou captura foi feita.

A mesma rodada UI passou no executável self-contained publicado. Render final de configurações inspecionado em `artifacts/gray-published-render-settings/settings-preview.png`.

## Referências newdesign e barra flutuante — 15/10/2026

As imagens `E:/AnyCraft/newdesign.png` e `newdesign servidor.png` foram localizadas e inspecionadas. O cliente passou a ter navegação lateral, painéis de identificação/conexão e seções recentes/favoritos; estados vazios e dispositivos vêm do perfil real, sem dispositivos fictícios na aplicação. Miniaturas usam ilustração própria de dispositivo, não wallpapers de outros produtos ou capturas não autorizadas. QR e contatos estão identificados como indisponíveis. O instalador servidor segue a lateral e os dois painéis de endereços/SHA, com controles reais do serviço e firewall preservados.

Durante sessão autorizada há uma barra compacta no topo da área de trabalho principal, sempre acima das janelas. A barra tem opacidade42% sem hover e100% com hover, timer, encerramento, envio/cancelamento de arquivo e detalhes recolhíveis. Recolher mantém o indicador e botão Encerrar visíveis. O host usa a própria janela de sessão como indicador; no viewer ela acompanha a janela de visualização. Fechar a sessão libera a barra/timer e mantém o encerramento único, sem ocultar controle remoto. Áudio e microfone são desabilitados com indicação de indisponibilidade, pois não existe transmissão de áudio nesta versão.

Testes isolados: navegação, busca, favoritos, rolagem de configurações/lateral/página/menu e Salvar em900×480 passaram. O teste específico da barra verificou dimensões compactas/topmost, eventos hover/leave e opacidade, clock real, expansão/recolhimento, callbacks de arquivo/encerramento, restrição de arquivos sem permissão, áudio desabilitado e fechamento em host/viewer (encerramento único). Não houve sessão, captura ou injeção de entrada real nessa rodada. Evidências: `artifacts/island-close-check/island-tests.txt`, `artifacts/island-design-ui-test/ui-tests.txt`. Renders cliente/configurações/barra e servidor em largura normal e680pixels foram inspecionados. Instalador servidor publicado validou payload; não foi reinstalado o serviço do usuário.

Na rodada publicada final, os testes UI e da barra também passaram. O preview com cartões usou três IDs fictícios apenas em um perfil isolado; foi corrigido o formato de data gerado pelo PowerShell no fixture, sem alteração do perfil do usuário ou do protocolo. Evidência: `artifacts/newdesign-published-ui-test/ui-tests.txt`, `artifacts/newdesign-published-island-test/island-tests.txt` e `artifacts/newdesign-cards-preview/ui-preview.png`.

## Proporções e DPI — correção posterior de15/10

Cliente, instalador cliente e instalador servidor agora calculam tamanho inicial a partir da área útil física do monitor da janela e GetDpiForWindow, limitando a abertura a90% da largura/altura útil e respeitando a barra de tarefas. Há reposicionamento e ajuste após mudança de DPI/resolução/área útil e restauração da janela. Os três executáveis declaram PerMonitorV2 com fallback PerMonitor. O cliente prefere1100×720, servidor1080×720, instalador680×650 em unidades lógicas, sempre limitados à área disponível. Os mínimos são reduzidos quando necessário. A janela de visualização remota também usa o ajuste; a barra compacta da sessão permanece separada.

O instalador do cliente passa a permitir redimensionamento. Os campos das duas etapas ficam em ScrollViewer; status e botões permanecem fixos. O app tem títulos/ID/painéis menores e lateral compacta em janela estreita. O modo estreito usa rótulo Recentes para evitar corte.

Validação:48 cenários calculados para quatro resoluções/áreas úteis e escalas100%,125%,150%,200% nos três perfis; todos respeitaram o limite físico de90%. Uma janela WPF real foi medida viaGetWindowRect e ficou dentro da área útil real. As escalas alternativas foram simuladas matematicamente, sem mudar a configuração do Windows. Instalador real em600×420 passou: campos rolam e botão Instalar permanece visível. Nenhuma instalação foi executada nesse teste. Ver `tests/layout/Program.cs` e a opção `RemoteDesk-Setup.exe --layout-test` (relatório emTEMP/RemoteDesk-setup-layout-test.txt).

Após o ajuste de DPI, o teste do menu verifica que a última ação está realmente visível, com ou sem necessidade de rolagem, em vez de exigir rolagem quando todos os itens já cabem. A altura máxima é recalculada ao abrir o menu. A rodada UI final em900×480 passou. Renders do cliente, servidor e instalador foram conferidos com tamanho limitado ao monitor; o instalador mantém os botões no rodapé e os campos em rolagem.

## Dispositivos e senha — 15/10/2026
34 verificações de acesso passaram (tests/access/artifacts/access-results.txt), 19 regressões do servidor passaram. Cliente validou cadastro, DPAPI sem senha em texto aberto, presença offline e rolagem. Ver docs/password-access.md para limites. Atualização elevada do serviço e dois PCs físicos permanecem pendentes.

## Correção firewall LAN
O lookup COM de regra ausente lança FileNotFoundException (HRESULT 0x80070002). O instalador agora trata esse código e cria a regra. Quatro testes passaram, incluindo lookup real do Windows sem alterar regras; erros de permissão continuam propagados. Aplicação elevada das regras permanece pendente.

## Janela compacta de configurações e ícone
Configurações agora usam janela nativa separada, 760x560 lógicos, redimensionável e limitada à área do monitor. O teste de UI confirma que o painel pertence à nova janela e a página principal permanece visível, além de rolagem e Salvar acessível em janela reduzida. Logo interna vetorial; ICO branco com R azul em 16/24/32/48/64/128/256 px.

## Controle velocidade/qualidade
22 testes do cliente passaram, incluindo resolução JPEG real e limites de presets. 18 verificações de integração do relay passaram, incluindo qualidade em sessão autorizada, limites e direção. Testes WPF passaram: orientação Qualidade à esquerda / Velocidade à direita; persistência; opções indisponíveis removidas; slider da barra roteia atualização com debounce. Veja transmission-quality.md. Não houve injeção de entrada.

A barra final tem marcador azul e Qualidade à esquerda / Velocidade à direita. Teste do template WPF com eventos DragDelta reais do Thumb confirmou que arrastar à direita reduz o nível de qualidade e à esquerda aumenta. O ajuste especial pela roda do mouse foi removido conforme esclarecimento do usuário.

## Fluxo de conexão, ponteiro e administrador
38 verificações de acesso e 19 regressões do servidor passaram. Testes WPF validaram janela de espera, senha/salvar, recusa, cancelamento, barra somente no destino, controles integrados no viewer e ponteiro azul. Reinício via runas preparado e validado sem executar elevação. UAC real e controle de aplicativo elevado pendentes. Veja connection-flow.md.

## Barra flutuante compacta
Barra normal reduzida de 600 para 360 px, com até 48 px de altura; modo minimizado de 180 px, com até 40 px de altura. Botões usam ícones vetoriais com descrições. Teste WPF acionou os botões reais de minimizar/restaurar, também com detalhes abertos, verificou dimensões, indicador e Encerrar visíveis e ausência de encerramento durante a troca. Renderizações dos três estados inspecionadas em artifacts/compact-island-tests. Permanecem os testes de transparência, timer, qualidade, arquivos autorizados e barra somente no destino.

## Tela da sessão e senha persistente
42 verificações de acesso e 19 regressões do servidor passaram. Testes WPF validaram senha curta com erro explícito, salvar/reabrir com checkbox ativo, campos vazios conservando senha e desativação. Viewer renderizado conforme conectado.png; ações de qualidade, ajuste, arquivo e encerramento roteadas. Relay testou probes/replies de latência e validação de direção/formato. Veja viewer-password.md.

## Diagnóstico real do acesso por senha — 15/10/2026
O servidor instalado respondia invalid session para access-policy e presence. O pedido ao ID autorizado 605077841 ficava pendente sem desafio. Hash confirmou binários antigos após rollback da atualização. A causa do rollback era reinserir o objeto COM de regra já existente (E_UNEXPECTED 0x8000FFFF). Corrigido ApplyRule para editar existentes e adicionar somente novas; seis testes passaram. O usuário confirmou atualização elevada bem-sucedida. Hash instalado passou a corresponder ao publicado. Diagnóstico real confirmou presença online, desafio com chave autenticada, prova com credencial já salva aceita e mensagem started. Sessão encerrada imediatamente, sem anexar mídia nem enviar entrada ou arquivos. Não houve exposição de senha. Cliente agora sinaliza servidor incompatível e não força modo por senha apenas pela preferência do dispositivo salvo.
