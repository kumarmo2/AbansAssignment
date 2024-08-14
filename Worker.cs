using System.Buffers.Binary;
using System.Linq;
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

        var lastSeq = intermediateResults[intermediateResults.Count - 1].Sequence;
        _logger.LogInformation("lastSeq: {lastSeq}", lastSeq);

        var presentSequences = intermediateResults.ToDictionary(i => i.Sequence);
        var finalResult = new List<ABXResponsePacket>();

        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        var client = new TcpClient();
        client.Connect(address, port);
        uint i = 1;
        while (i <= lastSeq)
        {
            if (presentSequences.ContainsKey(i))
            {
                finalResult.Add(presentSequences[i]);
                i++;
                continue;
            }
            try
            {
                _logger.LogInformation("packet i: {i} is missing, isClientConnected: {isClientConnected}", i, client.Connected);
                if (!client.Connected)
                {
                    client = new TcpClient();
                    client.Connect(address, port);
                    // _logger.LogInformation("connected again, isClientConnected: {isClientConnected}", client.Connected);
                }
                using (var stream = client.GetStream())
                {
                    // _logger.LogInformation(">>> got the stream");
                    stream.ReadTimeout = 500;
                    ABXRequest request = new ABXRequest { CallType = ABXRequest.RESEND_PACKET, ResendSeq = (byte)i };
                    // _logger.LogInformation("writing request for missing ResendSeq, seq: {i}", i);
                    request.WriteToSTream(stream);
                    // _logger.LogInformation(">>>>> write done");
                    // TODO: maybe add re-trying logic.
                    var (bytesRead, packet) = ReadPacket(singlePacketBuffer, stream);
                    // _logger.LogInformation(">>>>> read also done");
                    if (bytesRead == 0)
                    {
                        // TODO: add re-trying logic.
                    }
                    finalResult.Add(packet);
                    i++;
                }
            }
            catch (Exception)
            {
                // _logger.LogInformation("... should be retried for i: {i}", i);
            }
        }

        foreach (var item in finalResult)
        {
            _logger.LogInformation("seq: {seq}", item.Sequence);
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
        }
    }
    private (int, ABXResponsePacket) ReadPacket(Span<byte> singlePacketBuffer, Stream bufStream)
    {

        Span<char> chars = stackalloc char[4];
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
}
