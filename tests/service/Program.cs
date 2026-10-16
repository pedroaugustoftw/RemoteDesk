using RemoteDesk.Infrastructure;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
string root=Path.Combine(AppContext.BaseDirectory,"artifacts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var config=ServiceConfiguration.Setup(root,"192.168.7.55",applyAcl:false,migrate:false);
var loaded=ServiceConfiguration.Load(root);
if(loaded.Public!=config)throw new Exception("Configuração não corresponde.");
using var cert=new X509Certificate2(File.ReadAllBytes(config.CertificateFile),loaded.Private.CertificatePassword,X509KeyStorageFlags.EphemeralKeySet);
if(cert.GetCertHashString(HashAlgorithmName.SHA256)!=config.CertificateSha256)throw new Exception("SHA não corresponde.");
Console.WriteLine("PASS geração certificado IP LAN, PFX e DPAPI LocalMachine com impressão correspondente");
if(File.ReadAllText(Path.Combine(root,"lan.json")).Contains(loaded.Private.RelaySecret)||File.ReadAllText(Path.Combine(root,"lan.json")).Contains(loaded.Private.CertificatePassword))throw new Exception("Segredo na configuração pública.");
Console.WriteLine("PASS configuração pública não contém senha PFX nem segredo do relay");
if(ServiceConfiguration.Setup(root,"192.168.7.55",applyAcl:false,migrate:false)!=config)throw new Exception("Rotação indevida.");Console.WriteLine("PASS configuração existente preserva certificado e impressão");
try{ServiceConfiguration.Setup(root,"192.168.7.56",applyAcl:false,migrate:false);throw new Exception("IP diferente aceito.");}catch(IOException){}Console.WriteLine("PASS troca involuntária de IP rejeitada");
try{ServiceConfiguration.Setup(root,"127.0.0.1",applyAcl:false,migrate:false);throw new Exception("loopback aceito.");}catch(ArgumentException){}Console.WriteLine("PASS IP loopback rejeitado");
string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RemoteDesk","infrastructure");
if(ServiceConfiguration.Public(source) is {} original){string migration=Path.Combine(AppContext.BaseDirectory,"artifacts",Guid.NewGuid().ToString("N"));var migrated=ServiceConfiguration.Setup(migration,original.Ip,applyAcl:false,migrate:true);if(migrated.CertificateSha256!=original.CertificateSha256)throw new Exception("Migração trocou certificado.");_ = ServiceConfiguration.Load(migration);string registry=Path.Combine(source,"data","devices.json");if(File.Exists(registry)&&!File.ReadAllBytes(registry).SequenceEqual(File.ReadAllBytes(Path.Combine(migration,"data","devices.json"))))throw new Exception("Registro alterado.");Console.WriteLine("PASS migração LAN preserva certificado, impressão e registro de IDs; segredo regravado DPAPI Machine");}
Console.WriteLine("SCM registration/start not tested: administrator required. No service installed.");

string registryTest=Path.Combine(root,"data","devices.json");File.WriteAllText(registryTest,"{\"key\":\"123456789\"}");
var online=ServiceConfiguration.ConfigureNetwork(root,"vps","wss://remote.example/ws","wss://remote.example/relay/ws",2750);
if(online.ServerUri!=config.ServerUri||online.CertificateSha256!=config.CertificateSha256||ServiceConfiguration.Load(root).Private!=loaded.Private||File.ReadAllText(registryTest)!="{\"key\":\"123456789\"}")throw new Exception("Network change altered identity or secrets");
if(ServiceConfiguration.Public(root)!=online)throw new Exception("Public mode did not persist");
Console.WriteLine("PASS public mode persists without changing LAN certificate, IDs or secrets");
foreach(var bad in new[]{"http://example/ws","wss://user:secret@example/ws","wss://example/ws?token=x"})
{try{ServiceConfiguration.ConfigureNetwork(root,"vps",bad,"wss://example/relay/ws",2750);throw new Exception("Unsafe URL accepted");}catch(ArgumentException){}}
try{ServiceConfiguration.ConfigureNetwork(root,"vps","wss://example/ws","wss://example/relay/ws",5092);throw new Exception("Internal port reused");}catch(ArgumentException){}
Console.WriteLine("PASS invalid public addresses and colliding gateway port rejected");
var local=ServiceConfiguration.ConfigureNetwork(root,"local",online.PublicServerUri,online.PublicRelayUri,2750);
if(local.NetworkMode!="local"||local.PublicRelayUri!=online.PublicRelayUri)throw new Exception("Mode switching lost public configuration");
Console.WriteLine("PASS switch to LAN preserves public configuration for later reuse");
