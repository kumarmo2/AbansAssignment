namespace ABXConsoleClient;





public struct ABXRequest
{
    public static byte STREAM_ALL = 1;
    public static byte RESEND_PACKET = 2;
    public byte CallType { get; set; }
    public byte ResendSeq { get; set; }


    public void WriteToSTream<S>(S stream)
        where S : Stream
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        if (CallType == ABXRequest.STREAM_ALL)
        {
            stream.WriteByte(CallType);
            stream.Flush();
            return;
        }
        throw new NotImplementedException();
    }
}

// TODO: see if we can change this to a struct
// or if we can somehow avoid/minimize heap allocation. 
public class ABXResponsePacket
{
    public static int PACKET_SIZE_BYTES = 17;
    public uint Symbol { get; set; }
    public char OrderType { get; set; }
    public uint Quantity { get; set; }
    public uint Price { get; set; }
    public uint Sequence { get; set; }
}

