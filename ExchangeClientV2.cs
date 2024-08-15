using System.Buffers.Binary;
using System.Net.Sockets;

namespace ABXConsoleClient;

public class ExchangeClientV2 : IDisposable
{
    private TcpClient _tcp;
    private readonly string _address;
    // TODO: see if its possible to replace with buffStream
    // TODO: make the stream lazy;
    private NetworkStream _stream;
    private readonly int _port;
    private const int RETRIES = 5;

    public ExchangeClientV2(string address, int port)
    {

        _address = address;
        _port = port;
        _stream = GetNewStream();
    }
    private NetworkStream GetNewStream()
    {
        // if (_stream is not null)
        // {
        //     _stream.Dispose();
        // }
        if (_tcp is not null)
        {
            _tcp.GetStream().Close();
            _tcp.Close();
            _tcp.Dispose();
        }
        var tcp = new TcpClient();
        tcp.Connect(_address, _port);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;
        Console.WriteLine("connected now");
        _tcp = tcp;
        return stream;
    }

    private void Reconnect()
    {
        _stream = GetNewStream();
    }

    private (int, ABXResponsePacket) ReadPacketV2(Span<byte> singlePacketBuffer, NetworkStream stream)
    {

        Span<char> chars = stackalloc char[4];
        var readBytes = stream.Read(singlePacketBuffer);
        if (readBytes <= 0)
        {
            return (0, null);
        }
        if (readBytes != ABXResponsePacket.PACKET_SIZE_BYTES)
        {
            throw new Exception("wrong size of the packet;");
        }
        var symbolBytes = singlePacketBuffer.Slice(0, 4);
        for (var j = 0; j < 4; j++)
        {
            chars[j] = (char)symbolBytes[j];
        }
        var symbolString = new String(chars);
        // var symbol = BinaryPrimitives.ReadUInt32BigEndian(symbolBytes);
        var orderType = (char)singlePacketBuffer[4];
        var quantityBytes = singlePacketBuffer.Slice(5, 4);
        var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
        var priceBytes = singlePacketBuffer.Slice(9, 4);
        var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
        var seqBytes = singlePacketBuffer.Slice(13, 4);
        var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
        // _logger.LogInformation($"symbolString: {symbolString},  orderType: {orderType}, quantity: {quantity}, price: {price}, seq: {seq}");
        var packet = new ABXResponsePacket
        {
            Price = price,
            Symbol = symbolString,
            Quantity = quantity,
            Sequence = seq,
            OrderType = orderType,
        };
        return (readBytes, packet);
    }

    public Result<ABXResponsePacket, TimeoutException> GetPacket(byte seq)
    {
        var retryNum = 0;
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        while (retryNum < RETRIES)
        {
            try
            {
                Console.WriteLine($"get packet, retryNum: {retryNum}");
                ABXRequest request = new ABXRequest { CallType = ABXRequest.RESEND_PACKET, ResendSeq = (byte)seq };
                request.WriteToSTream(_stream);
                Console.WriteLine($">>>> wrote to the stream, will now read");
                var (bytesRead, packet) = ReadPacketV2(singlePacketBuffer, _stream);
                if (bytesRead == 0 && retryNum < RETRIES - 1)
                {
                    Console.WriteLine("... bytesRead from GetPacket were 0, reconnecting");
                    Reconnect();
                }
                else
                {
                    return new Result<ABXResponsePacket, TimeoutException>(packet);
                }
                // return new Result<ABXResponsePacket, TimeoutException>(packet);
            }
            catch (IOException e)
            {
                Console.WriteLine($"......caught IOException in client. will retry now,  {e.Message}");
                Reconnect();
            }
            retryNum++;
            if (retryNum > 0)
            {
                Thread.Sleep(retryNum * _stream.ReadTimeout);
            }
        }
        return new Result<ABXResponsePacket, TimeoutException>(new TimeoutException());
    }


    public Result<List<ABXResponsePacket>, TimeoutException> GetAllStreamResponse()
    {
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        var packets = new List<ABXResponsePacket>();
        var retryNum = 0;
        // TODO: take out the retrying logic at a common place?
        while (retryNum < ExchangeClientV2.RETRIES)
        {
            try
            {

                var request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
                request.WriteToSTream(_stream);
                while (true)
                {
                    var (bytesRead, packet) = ReadPacketV2(singlePacketBuffer, _stream);
                    if (bytesRead <= 0)
                    {
                        break;
                    }
                    packets.Add(packet);
                }
                return new Result<List<ABXResponsePacket>, TimeoutException>(packets);
            }
            catch (IOException e)
            {
                Console.WriteLine($"......caught IOException in client. will retry now,  {e.Message}");
                Reconnect();
            }
            retryNum++;
            if (retryNum > 0)
            {
                Thread.Sleep(retryNum * _stream.ReadTimeout);
            }
        }
        return new Result<List<ABXResponsePacket>, TimeoutException>(new TimeoutException());
    }

    public void Dispose()
    {
        // TODO: Implement dispose correctly.
        if (_stream is not null)
        {
            _stream.Dispose();
        }
        if (_tcp is not null)
        {
            _tcp.Dispose();
        }
    }
}
