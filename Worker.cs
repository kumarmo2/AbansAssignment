using System.Buffers.Binary;
using System.Linq;
using System.Net.Sockets;

namespace ABXConsoleClient;


public class Worker
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }
    public void doWorkV2()
    {
        var address = "127.0.0.1";
        var port = 3000;
        List<ABXResponsePacket> intermediateResults = new List<ABXResponsePacket>();
        using (var client = new ExchangeClientV2(address, port))
        {
            var allStreamResult = ExecuteAllStreamRequestV2(client);
            if (allStreamResult.Err != null)
            {
                throw new Exception("Even After retrying, could not connect to the exchange server");
            }
            intermediateResults = allStreamResult.Ok;
        }
        _logger.LogInformation(">>>> stream was fetched <<<<");
        Thread.Sleep(5000);
        intermediateResults.Sort((first, second) =>
                {
                    return (int)first.Sequence - (int)second.Sequence;
                });

        var lastSeq = intermediateResults[intermediateResults.Count - 1].Sequence;
        _logger.LogInformation("lastSeq: {lastSeq}", lastSeq);

        var presentSequences = intermediateResults.ToDictionary(i => i.Sequence);
        var finalResult = new List<ABXResponsePacket>();
        uint i = 1;
        using (var client = new ExchangeClientV2(address, port))
        {
            while (i <= lastSeq)
            {
                if (presentSequences.ContainsKey(i))
                {
                    finalResult.Add(presentSequences[i]);
                    i++;
                    continue;
                }
                // _logger.LogInformation("packet i: {i} is missing, isClientConnected: {isClientConnected}", i, client.Connected);
                _logger.LogInformation("packet i: {i} is missing", i);
                Thread.Sleep(5000);
                // _logger.LogInformation(">>> got the stream");
                var packetResult = client.GetPacket((byte)i);
                if (packetResult.Err != null)
                {
                    _logger.LogInformation("packet {i} could not be fetched even after retrying");
                    i++;
                    continue;
                }
                _logger.LogInformation("missing packet: {i} was fetched successfully", i);
                var packet = packetResult.Ok;
                finalResult.Add(packet);
                i++;
            }
            _logger.LogInformation("is finalResult null: {isNull}", finalResult is null);

            foreach (var item in finalResult)
            {
                _logger.LogInformation("seq: {seq}", item.Sequence);
            }

            var packets = System.Text.Json.JsonSerializer.Serialize(finalResult);
            var path = Directory.GetCurrentDirectory() + "/a.json";
            _logger.LogInformation("writing to the path: {path}", path);
            File.WriteAllText(path, packets);

        }
    }

    public void doWork()
    {
        var doneAllStreamRequest = false;
        var address = "127.0.0.1";
        var port = 3000;
        List<ABXResponsePacket> intermediateResults = new List<ABXResponsePacket>();
        using (var client = new ABXExchangeServerClient(address, port))
        {
            while (!doneAllStreamRequest)
            {
                var result = ExecuteAllStreamRequest(client);
                if (result.Err is not null)
                {
                    // _logger.LogInformation("...retrying");
                    continue;
                }
                intermediateResults = result.Ok;
                doneAllStreamRequest = true;
            }
            _logger.LogInformation(">>>> stream was fetched <<<<");
            Thread.Sleep(5000);
            intermediateResults.Sort((first, second) =>
                    {
                        return (int)first.Sequence - (int)second.Sequence;
                    });

            var lastSeq = intermediateResults[intermediateResults.Count - 1].Sequence;
            _logger.LogInformation("lastSeq: {lastSeq}", lastSeq);

            var presentSequences = intermediateResults.ToDictionary(i => i.Sequence);
            var finalResult = new List<ABXResponsePacket>();

            Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
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
                    // _logger.LogInformation("packet i: {i} is missing, isClientConnected: {isClientConnected}", i, client.Connected);
                    _logger.LogInformation("packet i: {i} is missing", i);
                    Thread.Sleep(5000);
                    // _logger.LogInformation(">>> got the stream");
                    ABXRequest request = new ABXRequest { CallType = ABXRequest.RESEND_PACKET, ResendSeq = (byte)i };
                    _logger.LogInformation("writing request for missing ResendSeq, seq: {i}", i);
                    client.WriteRequest(request);
                    // request.WriteToSTream(stream);
                    _logger.LogInformation(">>>>> write done, will read now after sleeping");
                    Thread.Sleep(5000);
                    // TODO: maybe add re-trying logic.

                    (var bytesRead, var packet) = ReadPacketV2(singlePacketBuffer, client);
                    if (bytesRead > 0)
                    {
                        _logger.LogInformation("missing packet: {i} was fetched", i);
                    }
                    // _logger.LogInformation(">>>>> read also done, bytesRead: {bytesRead}, will retry now", bytesRead);
                    if (bytesRead == 0)
                    {
                        continue;
                        // TODO: add re-trying logic.
                    }

                    finalResult.Add(packet);
                    i++;
                }
                catch (Exception)
                {
                    // _logger.LogInformation("... should be retried for i: {i}", i);
                }
            }

            _logger.LogInformation("is finalResult null: {isNull}", finalResult is null);

            foreach (var item in finalResult)
            {
                _logger.LogInformation("seq: {seq}", item.Sequence);
            }
        }
    }

    public Result<List<ABXResponsePacket>, TimeoutException> ExecuteAllStreamRequestV2(ExchangeClientV2 client)
    {
        return client.GetAllStreamResponse();
    }

    public Result<List<ABXResponsePacket>, TimeoutException> ExecuteAllStreamRequest(ABXExchangeServerClient client)
    {
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        var intermediateResults = new List<ABXResponsePacket>();

        var request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
        client.WriteRequest(request);
        _logger.LogInformation("wrote the request, will read the packet now");
        var readBytes = 0;
        while (true)
        {
            try
            {
                var (bytesRead, packet) = ReadPacketV2(singlePacketBuffer, client);
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
    private (int, ABXResponsePacket) ReadPacketV2(Span<byte> singlePacketBuffer, ABXExchangeServerClient client)
    {

        Span<char> chars = stackalloc char[4];
        var readBytes = client.Read(singlePacketBuffer);
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
