# Instalação e desinstalação no Windows

Os instaladores registram duas aplicações independentes em Configurações → Aplicativos → Aplicativos instalados (Windows 11), Aplicativos e recursos (Windows 10) e Programas e Recursos:

- RemoteDesk: instalação para o usuário atual em LocalAppData, registro de desinstalação HKCU.
- RemoteDesk Server: instalação para o computador em Program Files, serviço automático e registro HKLM; requer administrador.

As entradas mostram nome, versão, ícone, tamanho estimado e comando de desinstalação. Atualizações usam a mesma chave e não criam programas duplicados. O desinstalador nativo é distribuído dentro de cada pacote e instalado junto com os arquivos, de modo que remover o instalador baixado não impede desinstalar o programa.

## Instalações antigas

Execute o instalador atualizado para cadastrar a instalação existente. Há também reparo do registro, que adiciona o desinstalador e a entrada sem reiniciar o cliente ou o serviço:

```powershell
.\RemoteDesk.Uninstall.exe --register-client
.\RemoteDesk.Uninstall.exe --register-server
```

O reparo só aceita pastas com o marcador válido de uma instalação RemoteDesk. O servidor pede confirmação UAC. Feche e reabra a lista de Aplicativos instalados para atualizar a exibição.

## Desinstalação

Use Desinstalar na lista do Windows. O desinstalador pede confirmação antes de remover:

- Cliente: exige fechar o aplicativo, remove binários, atalhos e inicialização automática do usuário. Preserva identidade DPAPI, configurações e dispositivos salvos fora da pasta app.
- Servidor: exige administrador, confere a origem do serviço, interrompe e remove o serviço e suas regras de firewall, e remove os binários. Preserva ProgramData/RemoteDeskServer com certificado, configuração e registro de dispositivos.

Somente diretórios do programa e cópias de recuperação com marcador de propriedade são removidos. Pastas sem marcador e caminhos com links não são apagados. O desinstalador executa uma cópia temporária para liberar os arquivos instalados; a cópia administrativa é agendada para remoção na reinicialização.

Validação automatizada: metadados em chave isolada real do registro, atualização sem duplicar entradas, remoção de pasta de binários de teste preservando identidade e rejeição de pastas sem marcador ou raiz da unidade. Não foi desinstalado o servidor real para testar a remoção de serviço.
