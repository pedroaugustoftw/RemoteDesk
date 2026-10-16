# Equilíbrio entre velocidade e qualidade

Em Configurações → Exibição, arraste o marcador azul na barra: Qualidade à esquerda e Velocidade à direita. Clique Salvar para definir o padrão deste PC.

Durante uma sessão, abra os detalhes na barra flutuante (seta) e ajuste o mesmo controle. A alteração é aplicada à captura seguinte depois de uma pausa de 300 ms, sem encerrar a conexão. O computador que visualiza pode enviar o ajuste ao destino mesmo em uma sessão apenas de visualização. Nenhuma permissão de teclado, mouse ou arquivos é concedida por esse ajuste. O ajuste durante a sessão é temporário.

O extremo de velocidade usa JPEG 30%, largura máxima 800 px e até 20 FPS. O extremo de qualidade usa JPEG 90%, largura máxima 1920 px e até 8 FPS. Os valores intermediários variam gradualmente; a resolução também é limitada pelo monitor e a taxa efetiva depende de CPU e rede. Não se trata de controle automático de congestionamento.

Atualize o servidor/relay e os dois clientes para usar o ajuste remoto durante a sessão. A configuração local continua funcionando sem o novo comando de relay.

Foram removidos controles sem implementação: contatos, QR Code, Wake-On-LAN, gravação, áudio/microfone e opções fictícias de interface. Idioma e tema fixos aparecem como informação. Regras permanentes de segurança aparecem como texto, sem caixas de seleção desativadas. O envio de arquivo da barra é mostrado somente quando a sessão tem essa permissão.

Validação: 22 testes do cliente, incluindo captura JPEG real nos três níveis e limites; 18 testes de integração do relay, incluindo níveis 0/50/100 e rejeição de direção/valores inválidos; testes WPF de orientação da barra, persistência, ausência de opções desativadas, navegação/rolagem e barra de sessão. A validação entre dois PCs físicos permanece pendente.
