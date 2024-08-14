using System.Buffers.Binary;
using System.Net.Sockets;

namespace ABXConsoleClient;


public class CustomException : Exception { }
public class Worker
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    public void doWork()
    {
        var doneAllStreamRequest = false;
        var address = "127.0.0.1";
        var port = 3000;
        List<ABXResponsePacket> intermediateResults = new List<ABXResponsePacket>();
        while (!doneAllStreamRequest)
        {
            var result = ExecuteAllStreamRequest(address, port);
            _logger.LogInformation(".... here");
            if (result.Err is not null)
            {
                _logger.LogInformation("...retrting");
                continue;
            }
            intermediateResults = result.Ok;
            doneAllStreamRequest = true;
        }
        intermediateResults.Sort((first, second) =>
        {
            return (int)first.Sequence - (int)second.Sequence;
        });

        foreach (var item in intermediateResults)
        {
            _logger.LogInformation("seq: {seq}", item.Sequence);
        }

        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        using (var client = new TcpClient())
        {
            client.Connect(address, port);
            using (var stream = client.GetStream())
            {
                ABXRequest request = new ABXRequest { CallType = ABXRequest.RESEND_PACKET, ResendSeq = 1 };
                _logger.LogInformation("writing request for ResendSeq");
                request.WriteToSTream(stream);
                _logger.LogInformation("done writing request for ResendSeq");
                // TODO: maybe add re-trying logic.
                var (bytesRead, packet) = ReadPacket(singlePacketBuffer, stream);
                _logger.LogInformation("bytes read, {bytesRead} ", bytesRead);
                if (packet is not null)
                {
                    _logger.LogInformation("asked seq: {seq}", packet.Sequence);
                }
            }
        }

    }




    public Result<List<ABXResponsePacket>, TimeoutException> ExecuteAllStreamRequest(string address, int port)
    {
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        var intermediateResults = new List<ABXResponsePacket>();
        using (var client = new TcpClient())
        {
            client.Connect(address, port);
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 500;
                using (var bufStream = new BufferedStream(stream))
                {

                    var request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
                    request.WriteToSTream(bufStream);
                    _logger.LogInformation("wrote the request, will read the packet now");
                    var readBytes = 0;
                    while (true)
                    {
                        try
                        {
                            var (bytesRead, packet) = ReadPacket(singlePacketBuffer, bufStream);
                            if (bytesRead <= 0)
                            {
                                break;
                            }
                            intermediateResults.Add(packet);

                        }
                        catch (IOException e)
                        {
                            _logger.LogInformation("got IOException, trying again to read");
                            return new Result<List<ABXResponsePacket>, TimeoutException>(new TimeoutException());
                        }
                    }
                    return new Result<List<ABXResponsePacket>, TimeoutException>(intermediateResults);

                }
            }

            // if (bytesReceived == 0)
            // {
            //     _logger.LogInformation("recieved all the bytes");
            //     return;
            // }
            // _logger.LogInformation("bytesReceived: {bytesReceived}", bytesReceived);
            // if (bytesReceived != ABXResponsePacket.PACKET_SIZE_BYTES)
            // {
            //     throw new Exception("wrong packet ")
            // }
            // using (var stream = tcpClient.GetStream())
            // {
            //     // ABXRequest request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
            //
            // }
        }
        _logger.LogInformation("...... ending");
    }
    private (int, ABXResponsePacket) ReadPacket(Span<byte> singlePacketBuffer, Stream bufStream)
    {

        var readBytes = bufStream.Read(singlePacketBuffer);
        if (readBytes <= 0)
        {
            return (0, null);
        }
        if (readBytes != ABXResponsePacket.PACKET_SIZE_BYTES)
        {
            throw new Exception("wrong size of the packet;");
        }
        var symbolBytes = singlePacketBuffer.Slice(0, 4);
        var symbol = BinaryPrimitives.ReadUInt32BigEndian(symbolBytes);
        var orderType = (char)singlePacketBuffer[4];
        var quantityBytes = singlePacketBuffer.Slice(5, 4);
        var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
        var priceBytes = singlePacketBuffer.Slice(9, 4);
        var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
        var seqBytes = singlePacketBuffer.Slice(13, 4);
        var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
        _logger.LogInformation($"symbol: {symbol}, orderType: {orderType}, quantity: {quantity}, price: {price}, seq: {seq}");
        var packet = new ABXResponsePacket
        {
            Price = price,
            Symbol = symbol,
            Quantity = quantity,
            Sequence = seq,
            OrderType = orderType,
        };
        return (readBytes, packet);
    }
}
// var symbolBytes = singlePacketBuffer.Slice(0, 4);
// var symbol = BinaryPrimitives.ReadUInt32BigEndian(symbolBytes);
// var orderType = (char)singlePacketBuffer[4];
// var quantityBytes = singlePacketBuffer.Slice(5, 4);
// var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
// var priceBytes = singlePacketBuffer.Slice(9, 4);
// var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
// var seqBytes = singlePacketBuffer.Slice(9, 4);
// var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
// _logger.LogInformation($"symbol: {symbol}, orderType: {orderType}, quantity: {quantity}, price: {price}, seq: {seq}");
