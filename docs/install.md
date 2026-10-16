# Compilação e instalação do RemoteDesk

## Compilar no Windows

Requer Windows 10/11 e SDK .NET 8. Cliente e instalador publicados incluem o runtime; servidor, relay e ferramenta LAN usam .NET 8 disponível na máquina que hospeda a infraestrutura.

```powershell
./scripts/publish.ps1 -Dotnet 'E:\AnyCraft\.tools\dotnet\dotnet.exe'
```

Saídas em `artifacts/win-x64/`: `client/RemoteDesk.exe`, `installer/RemoteDesk-Setup.exe`, `server/`, `relay/`, `lan-tools/`.

O script executa `RemoteDesk-Setup.exe --verify-payload`, que valida caminhos e lê o ZIP embedded sem instalar nem criar atalhos. Código 0 indica executável presente e arquivos legíveis. Testes de política e atualização em diretório temporário: `dotnet run --project installer/tests/RemoteDesk.InstallerTests.csproj -c Release`. Essa verificação não substitui instalação numa máquina separada.

## Instalar o cliente

Execute `RemoteDesk-Setup.exe` e clique **Instalar**. Os binários ficam em `%LocalAppData%\RemoteDesk\app`; os atalhos são criados na área de trabalho e menu Iniciar do usuário atual. Não há serviço, elevação, inicialização automática ou abertura automática do cliente. Feche o cliente antes de atualizar. Os binários ainda não possuem assinatura Authenticode.

O instalador valida o pacote inteiro antes de extrair, rejeita caminhos externos, ADS, links e conflitos de nomes; prepara uma pasta temporária e troca somente o diretório de binários `app`. Configurações `settings.json`, identidade protegida por DPAPI `identity.dpapi` e histórico/logs ficam no diretório pai e são preservados. Um `app` preexistente sem marcador de instalação é recusado. Se a troca do diretório falhar, o instalador tenta restaurar os binários anteriores; falhas posteriores na criação de atalhos podem ser corrigidas executando novamente. A proteção contra links não estabelece isolamento contra um atacante já executando como o mesmo usuário Windows.

Para remover os binários, feche o cliente e remova somente `%LocalAppData%\RemoteDesk\app` e os atalhos `RemoteDesk.lnk`. Preserve o diretório pai para manter a identidade, configurações e infraestrutura LAN. Removê-lo por completo apaga também esses dados.

## Hospedar servidor e relay na mesma rede Windows

Não é necessária VPS. Escolha um PC Windows da rede para executar a infraestrutura com o mesmo usuário Windows. Não há acesso não supervisionado nos clientes.

```powershell
./scripts/Setup-Lan.ps1 -Ip 192.168.1.10
./scripts/Start-Lan.ps1
```

Substitua `192.168.1.10` pelo IPv4 real desse PC. Sem `-Ip`, o script tenta a interface da rota IPv4 padrão; confirme o IP mostrado. Se o IP mudar, gere uma nova configuração em outro diretório e atualize endereço e fingerprint nos clientes.

O setup LAN gera um certificado autoassinado com SAN do IP, hostname e localhost, validade de 90 dias e fingerprint SHA256. A senha do PFX e o segredo de autorização do relay são aleatórios, protegidos por DPAPI CurrentUser. A ferramenta tenta restringir a ACL ao usuário atual; se o ambiente negar essa alteração, informa a limitação e mantém a proteção DPAPI e a senha forte do PFX. Não são impressos nem colocados na linha de comando. Os processos recebem os segredos apenas em seu ambiente.

Configuração padrão: `%LocalAppData%\RemoteDesk\infrastructure`. `lan.json` contém apenas informações públicas; `lan.pfx` contém a chave TLS cifrada; `secrets.dpapi` só pode ser aberto pelo usuário Windows que fez o setup. Preserve os três arquivos e `data/devices.json`; perder o registro de dispositivos troca os IDs do servidor. Não copie a configuração DPAPI para outra conta ou computador; execute o setup nessa conta.

Portas: servidor **TLS 5081**, relay **TLS 5091**, API privada do relay **HTTP 5092 somente em 127.0.0.1**. A API exige um segredo forte compartilhado; não é uma porta de acesso remoto. Não alteramos firewall nem a confiança de certificados do Windows automaticamente. Para conexões de outro computador, o operador precisa permitir TCP 5081/5091 no firewall apenas para a rede privada, quando necessário. Não exponha 5092 nem encaminhe estas portas pela internet nesta implantação LAN.

Nos dois clientes configure o servidor `wss://192.168.1.10:5081/ws` e preencha os fingerprints SHA256 do servidor e do relay com o valor exibido pelo setup LAN. Verifique o fingerprint por um canal confiável: a confiança é no certificado específico, sem desativar a validação TLS global ou importar uma CA. O relay anunciado pelo servidor usa `wss://192.168.1.10:5091/ws` e o mesmo certificado. Cada sessão ainda exige consentimento explícito do computador de destino.

`Start-Lan.ps1` inicia um supervisor e os dois processos no usuário atual, sem serviço, valida HTTPS com fingerprint e informa os logs. Para encerrar:

```powershell
./scripts/Stop-Lan.ps1
```

O script confere PID, horário de início, caminho do executável e assembly antes de parar cada processo, evitando encerrar outros processos .NET. O supervisor encerra o outro componente se um componente sair. Logs são `server.log`, `server.error.log`, `relay.log` e `relay.error.log` no diretório de configuração.

## Internet — alternativa futura

Há um modelo opcional de Nginx/Docker Compose em `relay/`, documentado no README desse diretório. Essa alternativa precisa de domínio, certificados e uma máquina externa; não é necessária para a rede local e não foi validada numa VPS. O relay próprio é independente; ICE/STUN/TURN, conexão direta e criptografia ponta a ponta não estão implementados. TLS protege os dois saltos, e o operador do relay consegue acessar o conteúdo.

## Limites de validação

Dois computadores Windows físicos, múltiplos monitores e conexões externas precisam ser validados nesses ambientes. Testes locais e de protocolo, inclusive TLS, não devem ser apresentados como prova desses cenários. Recursos e resultados efetivos são registrados no relatório de testes do projeto.

## Servidor durante a instalação do cliente

O instalador do cliente apresenta duas etapas. Na etapa Conexão, informe o servidor principal, por exemplo `wss://192.168.7.55:5081/ws`. Para certificado autoassinado LAN, copie o SHA256 público mostrado pelo instalador servidor. Se servidor e relay usam o mesmo certificado, mantenha a opção correspondente marcada. Campos vazios usam a confiança normal de certificados do Windows; não desativam TLS.

Em atualização, a configuração é carregada e preservada se não editada. Alterar a conexão mantém identidade, histórico, apelidos e demais campos. A configuração só é gravada após os arquivos. O instalador não abre o cliente automaticamente. O ID aparece quando o cliente se autentica no servidor; instalar os binários por si só não registra o computador.

## Inicialização automática e firewall — 15/10/2026

O instalador servidor configura duas regras de entrada do Windows Firewall, para os executáveis instalados em Program Files, somente TCP nas portas do servidor/relay, IP local configurado e origem LocalSubnet. Não desativa o firewall nem expõe a API interna. A regra vale também quando a rede Windows está marcada como pública, mantendo a restrição de sub-rede. Exige a mesma elevação administrativa da instalação. Para serviço já instalado, use o botão Configurar firewall LAN; não é necessário remover o serviço. O serviço permanece automático na inicialização do Windows.

O instalador cliente oferece Iniciar com o Windows, minimizado na bandeja (marcado por padrão). Registra somente RemoteDesk na chave Run do usuário atual, com argumento --minimized. Inicia após login nessa conta, não antes do login. É uma aplicação interativa na bandeja, sem serviço de captura. Duplo clique no ícone ou abrir o atalho novamente restaura a janela. O menu da bandeja permite desativar a inicialização e sair. Minimizar mantém o processo; fechar a janela ou Finalizar encerra. A solicitação recebida restaura a janela e exige aceitação, com indicador de sessão e encerramento preservados. Não há acesso não supervisionado.
