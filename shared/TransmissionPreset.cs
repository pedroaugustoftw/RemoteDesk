namespace RemoteDesk.Shared;
public record TransmissionPreset(int Quality,int Width,int Fps)
{
    public static TransmissionPreset FromLevel(int level)
    {
        if(level is <0 or >100)throw new ArgumentOutOfRangeException(nameof(level));
        return new(30+(int)Math.Round(level*.6),800+(int)Math.Round(level*11.2),20-(int)Math.Round(level*.12));
    }
    public static int LevelFromQuality(int quality)=>Math.Clamp((int)Math.Round((quality-30)/.6),0,100);
}
