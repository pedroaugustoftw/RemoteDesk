# Tela da sessão e configuração de senha

A tela de quem acessa segue conectado.png: cabeçalho com identidade e sessão, imagem central, ferramentas à direita e ações no rodapé. Somente comandos implementados são mostrados: maximizar/tela cheia, ajuste à janela ou tamanho original, foco no teclado remoto autorizado, qualidade, transferência autorizada e encerramento. A barra flutuante permanece somente no computador acessado.

A resolução vem dos quadros recebidos. FPS e Mbps representam os quadros JPEG apresentados (sem overhead do transporte ou quadros descartados antes da apresentação). A latência é a ida e volta de uma mensagem entre os clientes através do transporte da sessão; não é um valor fixo nem uma classificação de qualidade inventada.

## Configurar acesso por senha

No computador de destino, na mesma conta Windows usada para executar o cliente:

1. Abra Configurações → Acesso por senha.
2. Marque Permitir acesso com senha sem confirmação local.
3. Digite uma senha de pelo menos 12 caracteres nos dois campos; escolha as permissões.
4. Clique Salvar e confira a mensagem de confirmação.

Ao reabrir, a opção deve continuar marcada e aparece Senha já cadastrada. Os campos vazios não indicam perda da senha: o programa guarda um verificador protegido e não recupera a senha original. Deixe ambos vazios para manter a senha e salvar somente permissões. Senha curta, confirmação diferente e alteração durante uma sessão geram erro explícito, sem substituir uma política válida.

O cliente agora aguarda confirmação do servidor ao publicar a política. Se o servidor não confirmar, a tela informa que a configuração foi salva localmente e solicita atualização do servidor. Sem conexão, informa que aguarda o servidor. O destino também exige o desafio de senha a partir da sua política local, evitando cair em aceite manual apenas porque o anúncio ainda não chegou ao servidor.

Atualize primeiro RemoteDesk-Server-Setup.exe e depois RemoteDesk-Setup.exe nos dois PCs. Mudanças de conta Windows usam outro perfil DPAPI e outra configuração.

## Validação

42 verificações de acesso e 19 regressões do servidor passaram. A integração do relay validou o encaminhamento dos probes de latência e rejeição de formato/direção inválidos, além de qualidade e permissões. O teste WPF validou cadastro da senha, erro de senha curta, reabertura com checkbox marcado, campos vazios, conservação do verificador e desativação. A nova tela foi renderizada e inspecionada usando imagem de teste, sem copiar a captura de referência para o produto. Testes também acionaram ajuste de imagem, painel de qualidade e botões de transferência/encerramento.

A validação física entre dois computadores continua pendente.
