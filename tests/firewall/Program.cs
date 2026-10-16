using System.Runtime.InteropServices;
using RemoteDesk.ServerSetup;
dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
try
{
    var marker=new object();bool created=false;
    var result=ServerFirewall.ExistingOrNew(()=>policy.Rules.Item("RemoteDesk-Missing-Test-"+Guid.NewGuid().ToString("N")),()=>{created=true;return marker;});
    if(!created||!ReferenceEquals(result,marker))throw new Exception("Missing rule was not handled.");
    Console.WriteLine("PASS real Windows missing-rule lookup handled without firewall mutation");
    result=ServerFirewall.ExistingOrNew(()=>marker,()=>throw new Exception("Existing rule replaced."));
    if(!ReferenceEquals(result,marker))throw new Exception("Existing rule lost.");
    Console.WriteLine("PASS existing rule preserved");
    try{ServerFirewall.ExistingOrNew(()=>throw new UnauthorizedAccessException(),()=>marker);throw new Exception("Permission error suppressed.");}catch(UnauthorizedAccessException){}
    Console.WriteLine("PASS permission errors propagate");
    result=ServerFirewall.ExistingOrNew(()=>throw new COMException("missing",unchecked((int)0x80070002)),()=>marker);
    if(!ReferenceEquals(result,marker))throw new Exception("COM fallback failed.");
    Console.WriteLine("PASS COM missing-rule fallback handled");
    int configured=0,added=0;
    ServerFirewall.ApplyRule(()=>marker,()=>throw new Exception("Existing rule replaced."),_=>configured++,_=>throw new COMException("Object already in collection",unchecked((int)0x8000FFFF)));
    if(configured!=1)throw new Exception("Existing rule not configured.");Console.WriteLine("PASS updating existing rule does not re-add COM object (E_UNEXPECTED regression)");
    ServerFirewall.ApplyRule(()=>throw new FileNotFoundException(),()=>marker,_=>configured++,_=>added++);
    if(configured!=2||added!=1)throw new Exception("New rule not added exactly once.");Console.WriteLine("PASS new rule configured and added once");
}
finally{Marshal.FinalReleaseComObject(policy);}
