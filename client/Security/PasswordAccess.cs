using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteDesk.Shared;
namespace RemoteDesk.Client.Security;
public record PasswordProof(string SessionId,string Nonce,string Password);
public record AccessPolicy(bool Enabled,string Salt,string Verifier,Permissions Permissions,int Failures=0,DateTime LockedUntil=default);
public sealed class PasswordAccess(string directory)
{
    const int Iterations=600000;
    readonly object gate=new();
    string PathName=>System.IO.Path.Combine(directory,"password-access.dpapi");
    public AccessPolicy? Read()
    {
        lock(gate)
        {
            if(!File.Exists(PathName))return null;
            var data=ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser);
            try{return JsonSerializer.Deserialize<AccessPolicy>(data);}finally{CryptographicOperations.ZeroMemory(data);}
        }
    }
    public void Configure(string password,Permissions permissions)
    {
        if(password.Length<12||Encoding.UTF8.GetByteCount(password)>96)throw new ArgumentException("Use uma senha de pelo menos 12 caracteres e até 96 bytes UTF-8.");
        if(!permissions.View||!permissions.IsSubsetOf(new(true,true,true)))throw new ArgumentException("Permissões inválidas.");
        byte[] salt=RandomNumberGenerator.GetBytes(32),verifier=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        try{Write(new(true,Convert.ToBase64String(salt),Convert.ToBase64String(verifier),permissions));}finally{CryptographicOperations.ZeroMemory(verifier);}
    }
    public void Disable(){lock(gate){if(File.Exists(PathName))File.Delete(PathName);}}
    public void UpdatePermissions(Permissions permissions)
    {
        if(!permissions.View||!permissions.IsSubsetOf(new(true,true,true)))throw new ArgumentException("Permissões inválidas.");
        lock(gate){var policy=Read()??throw new InvalidOperationException("Cadastre uma senha primeiro.");Write(policy with{Enabled=true,Permissions=permissions});}
    }
    public bool Available=>Read() is {Enabled:true} policy&&policy.LockedUntil<=DateTime.UtcNow;
    public Permissions? Verify(PasswordProof proof,string session,string nonce)
    {
        lock(gate)
        {
            var policy=Read();if(policy is not {Enabled:true}||policy.LockedUntil>DateTime.UtcNow)return null;
            bool valid=false;byte[]? derived=null;
            try
            {
                if(proof.SessionId==session&&proof.Nonce==nonce&&proof.Password is {Length:<=96}&&Encoding.UTF8.GetByteCount(proof.Password)<=96)
                {derived=Rfc2898DeriveBytes.Pbkdf2(proof.Password,Convert.FromBase64String(policy.Salt),Iterations,HashAlgorithmName.SHA256,32);valid=CryptographicOperations.FixedTimeEquals(derived,Convert.FromBase64String(policy.Verifier));}
            }
            finally{if(derived!=null)CryptographicOperations.ZeroMemory(derived);}
            int failures=valid?0:policy.Failures+1;
            Write(policy with{Failures=failures>=5?0:failures,LockedUntil=failures>=5?DateTime.UtcNow.AddMinutes(5):default});
            return valid?policy.Permissions:null;
        }
    }
    public void InvalidAttempt()
    {
        Verify(new("","",""),"invalid-session","invalid-nonce");
    }
    void Write(AccessPolicy policy)
    {
        lock(gate)
        {
            Directory.CreateDirectory(directory);var clear=JsonSerializer.SerializeToUtf8Bytes(policy);
            try{var data=ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser);File.WriteAllBytes(PathName+".tmp",data);File.Move(PathName+".tmp",PathName,true);}finally{CryptographicOperations.ZeroMemory(clear);}
        }
    }
    public static string Seal(string password)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password),null,DataProtectionScope.CurrentUser));
    public static string Unseal(string encrypted)
    {
        var clear=ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser);
        try{return Encoding.UTF8.GetString(clear);}finally{CryptographicOperations.ZeroMemory(clear);}
    }
    public static string EncryptProof(string publicKey,PasswordProof proof)
    {
        using var rsa=RSA.Create();rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out _);
        if(rsa.KeySize<2048||rsa.KeySize>4096)throw new CryptographicException("Chave do destino inválida.");
        if(!Guid.TryParseExact(proof.SessionId,"N",out _)||proof.Nonce.Length!=44||Convert.FromBase64String(proof.Nonce).Length!=32||Encoding.UTF8.GetByteCount(proof.Password)>96)throw new ArgumentException("Prova inválida.");
        var clear=Encoding.UTF8.GetBytes(proof.SessionId+"\n"+proof.Nonce+"\n"+proof.Password);
        try{return Convert.ToBase64String(rsa.Encrypt(clear,RSAEncryptionPadding.OaepSHA256));}finally{CryptographicOperations.ZeroMemory(clear);}
    }
    public static PasswordProof DecodeProof(byte[] clear)
    {
        if(clear.Length>256)throw new FormatException("Prova excedida.");
        var parts=Encoding.UTF8.GetString(clear).Split('\n',3);
        if(parts.Length!=3||!Guid.TryParseExact(parts[0],"N",out _)||parts[1].Length!=44||Convert.FromBase64String(parts[1]).Length!=32)throw new FormatException("Prova inválida.");
        return new(parts[0],parts[1],parts[2]);
    }
}
