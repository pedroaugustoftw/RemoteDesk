# Instalador do cliente RemoteDesk

O instalador tem duas etapas: **Instalação** apresenta o destino do aplicativo; **Conexão** pede o endereço do servidor principal e fingerprints SHA256 opcionais do servidor e relay. O checkbox permite usar o mesmo certificado para ambos. Não há endereço desta máquina ou fingerprint local embutido no pacote.

Na primeira instalação o endereço é obrigatório. Informe `wss://IP-ou-host:5081/ws`. Conexões `ws://` são aceitas apenas para `localhost` e IPs de loopback. Credenciais na URL, parâmetros, fragmentos, outras rotas e portas inválidas são recusados. Fingerprints aceitam 64 dígitos hexadecimais, incluindo separadores `:`; para certificados confiáveis pelo Windows podem ficar vazios. Certificados autoassinados precisam do fingerprint conferido com o administrador.

Atualizações carregam `settings.json` do perfil atual. Se nada for editado, o arquivo é preservado byte a byte. Se a conexão for alterada, somente `Server`, `ServerCertificateSha256` e `RelayCertificateSha256` são atualizados por merge JSON. Apelido, identidade, dispositivos recentes e campos desconhecidos são preservados; a chave `identity.dpapi` não é aberta nem alterada. Configuração inválida já existente impede a alteração para evitar perda de dados.

A configuração é gravada por substituição atômica somente depois que a instalação dos binários termina. Se essa gravação falhar, a configuração antiga permanece; os binários podem já ter sido atualizados, e o instalador informa falha para permitir nova tentativa. O cliente não é aberto automaticamente. O instalador não modifica firewall, serviços ou confiança de certificados do Windows e não testa a disponibilidade do servidor.

## Verificação sem instalar

```powershell
dotnet run --project installer/tests/RemoteDesk.InstallerTests.csproj -c Release
dotnet build installer/RemoteDesk.Setup.csproj -c Release
```

O setup mantém `--verify-payload` e `--expected-client-sha256 HASH`. Para gerar imagens da interface sem ler a configuração pessoal ou instalar:

```powershell
RemoteDesk-Setup.exe --render installation-preview.png
RemoteDesk-Setup.exe --render-connection connection-preview.png
```

Os testes fazem extração/atualização apenas em diretórios temporários exclusivos, sem criar atalhos ou iniciar clientes. A validação de formato não prova que o servidor existe ou que o fingerprint pertence a ele. Não foi realizada instalação no perfil real nesta alteração.
