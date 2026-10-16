namespace RemoteDesk.Client.Streaming;
public static class JpegHeader
{
    public static void Validate(ReadOnlySpan<byte> data)
    {
        if(data.Length<4||data[0]!=255||data[1]!=216)throw new IOException("Frame não é JPEG.");
        int p=2;
        while(p+3<data.Length)
        {
            if(data[p++]!=255)throw new IOException("JPEG inválido.");
            while(p<data.Length&&data[p]==255)p++;
            if(p>=data.Length)break;int marker=data[p++];
            if(marker is 216 or 217 or >=208 and <=215)continue;
            if(p+1>=data.Length)break;int size=(data[p]<<8)|data[p+1];
            if(size<2||p+size>data.Length)throw new IOException("JPEG truncado.");
            if(marker is 192 or 193 or 194)
            {
                if(size<8)break;int height=(data[p+3]<<8)|data[p+4],width=(data[p+5]<<8)|data[p+6];
                if(width<1||height<1||width>3840||height>3840||(long)width*height>8300000)throw new IOException("Dimensões do frame fora dos limites.");return;
            }
            if(marker==218)break;p+=size;
        }
        throw new IOException("JPEG sem dimensões válidas.");
    }
}
