using System.Buffers.Binary;
using System.Net.Sockets;

namespace ABXConsoleClient;

public class Worker
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    public void Execute()
    {
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        // Span<byte> singlePacketBuffer = new byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        using (var tcpClient = new TcpClient())
        {


            tcpClient.Connect("127.0.0.1", 3000);
            using (var stream = tcpClient.GetStream())
            {
                ABXRequest request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
                _logger.LogInformation("before sending rewquest, stream DataAvailable: {DataAvailable}", stream.DataAvailable);
                request.WriteToSTream(stream);
                _logger.LogInformation("after sending request, data available: {steamDataAvailable}", stream.DataAvailable);
                _logger.LogInformation("request written");
                while (true)
                {
                    _logger.LogInformation("...reading");
                    var bytesRead = stream.Read(singlePacketBuffer);
                    _logger.LogInformation("bytes reader: {bytesRead} ", bytesRead);
                    if (bytesRead <= 0)
                    {
                        _logger.LogInformation("found the end of the response");
                        // Thread.Sleep(5000);
                        break;
                    }
                    var symbolBytes = singlePacketBuffer.Slice(0, 4);
                    var symbol = BinaryPrimitives.ReadUInt32BigEndian(symbolBytes);
                    var orderType = (char)singlePacketBuffer[4];
                    var quantityBytes = singlePacketBuffer.Slice(5, 4);
                    var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
                    var priceBytes = singlePacketBuffer.Slice(9, 4);
                    var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
                    var seqBytes = singlePacketBuffer.Slice(9, 4);
                    var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
                    _logger.LogInformation($"symbol: {symbol}, orderType: {orderType}, quantity: {quantity}, price: {price}, seq: {seq}");
                }

                // while ()

                // }
                // throw new NotImplementedException("Pending logic");
            }
        }
        _logger.LogInformation("...... ending");
    }
}
