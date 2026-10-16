# Transferência de arquivos

A página segue a referência visual fornecida: menu lateral, explorador local à esquerda, remoto à direita, setas de direção, fila e confirmação do destino com progresso. A identidade azul e os vetores próprios do RemoteDesk são mantidos.

## Atualização

Instale o RemoteDesk-Setup.exe atualizado nos dois PCs e atualize os componentes do servidor com a versão server-installer-files. Servidor e relay precisam conhecer as novas mensagens de listagem e criação de pastas. O certificado, o modo de rede e os IDs existentes são preservados conforme a configuração escolhida no instalador.

## Uso

1. Conecte ao PC remoto com a permissão **navegar, enviar e receber arquivos**.
2. No computador que está acessando, clique em **Transferir arquivo** na sessão. A opção também está no menu principal durante a sessão.
3. Navegue pelas pastas com duplo clique, endereço, voltar, avançar e subir. O painel remoto começa nas unidades locais disponíveis.
4. Marque arquivos no painel de origem. Use → para enviar ou ← para receber. A fila permite retirar itens antes de iniciar.
5. Abra a pasta de destino no outro painel. Marque **Substituir arquivos existentes** somente se desejar sobrescrever.
6. Clique em **Iniciar transferência** e confirme o destino. O recebimento termina após conferir SHA-256. Cancelar remove os arquivos temporários.

A página de arquivos aparece somente no computador que acessa. O Dynamic Island no computador acessado mantém o indicador, encerramento, progresso e cancelamento, sem botão para iniciar o explorador.

## Transporte e limites

Conforme a escolha do usuário, a transferência mantém o transporte WebSocket protegido da sessão, compatível com o túnel atual; não utiliza FTP. Blocos de 64 KiB, conferência SHA-256 e limite de 2 GiB por arquivo. A navegação requer permissão de arquivos; a permissão de visualizar a tela sozinha não permite navegar.

São transferidos arquivos individuais ou múltiplos arquivos em fila. Pastas podem ser abertas e criadas, mas não são copiadas recursivamente. A listagem tem páginas de 100 itens, com Carregar mais. Unidades de rede, caminhos de dispositivo e links de redirecionamento são rejeitados. As permissões do usuário Windows continuam valendo.

## Validação

Testes locais cobrem listagem e paginação reais, criação remota de pastas, envio e recebimento de arquivos aleatórios, integridade, cancelamento, temporários, sobrescrita, autorização, direção das mensagens e controles da interface nos tamanhos normal e compacto. A integração do gateway valida novas mensagens através do relay WebSocket e bloqueia navegação iniciada pelo host. O teste entre dois PCs físicos pela internet permanece pendente.
