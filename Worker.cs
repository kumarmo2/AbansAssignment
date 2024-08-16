using System.Buffers.Binary;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ABXConsoleClient;


public class Worker
{
    private readonly ILogger<Worker> _logger;
    private readonly ExchangeServerConnectionConfig _exchangeServerConfig;
    private readonly IABXExchangeServerClient client;

    public Worker(ILogger<Worker> logger, IOptions<ExchangeServerConnectionConfig> config, IABXExchangeServerClient c)
    {
        _logger = logger;
        _exchangeServerConfig = config.Value;
        client = c;
    }

    public Result<bool, Exception> doWorkV2()
    {
        _logger.LogInformation("exchange server host: {host}, port: {port}", _exchangeServerConfig.Host, _exchangeServerConfig.Port);
        List<ABXResponsePacket> intermediateResults = new List<ABXResponsePacket>();
        var allStreamResult = client.GetAllStreamResponse();
        if (allStreamResult.Err != null)
        {
            return new Result<bool, Exception>(allStreamResult.Err);
        }
        intermediateResults = allStreamResult.Ok;
        var waitMs = 2000;
        _logger.LogInformation(">>>> stream was fetched. Just waiting for {waitMs} milliseconds <<<<", waitMs);
        Thread.Sleep(waitMs);
        intermediateResults.Sort((first, second) =>
                {
                    return (int)first.Sequence - (int)second.Sequence;
                });

        var lastSeq = intermediateResults[intermediateResults.Count - 1].Sequence;
        _logger.LogInformation("lastSeq: {lastSeq}", lastSeq);

        var presentSequences = intermediateResults.ToDictionary(i => i.Sequence);
        var finalResult = new List<ABXResponsePacket>();
        uint i = 1;
        while (i <= lastSeq)
        {
            if (presentSequences.ContainsKey(i))
            {
                finalResult.Add(presentSequences[i]);
                i++;
                continue;
            }
            _logger.LogError("packet i: {i} is missing", i);
            var packetResult = client.GetPacket((byte)i);
            if (packetResult.Err != null)
            {
                _logger.LogInformation("packet {i} could not be fetched even after retrying");
                return new Result<bool, Exception>(packetResult.Err);
            }
            _logger.LogInformation("missing packet: {i} was fetched successfully", i);
            var packet = packetResult.Ok;
            finalResult.Add(packet);
            i++;
        }
        _logger.LogInformation("is finalResult null: {isNull}", finalResult is null);

        foreach (var item in finalResult)
        {
            _logger.LogDebug("seq: {seq}", item.Sequence);
        }

        var packets = System.Text.Json.JsonSerializer.Serialize(finalResult);
        var path = Directory.GetCurrentDirectory() + "/dist/a.json";
        _logger.LogInformation("writing to the path: {path}", path);
        File.WriteAllText(path, packets);
        return new Result<bool, Exception>(true);
    }
}
