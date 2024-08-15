namespace ABXConsoleClient;
using System.Net.Sockets;

public class ABXExchangeServerClient : IDisposable
{
    private TcpClient _tcp;
    private readonly string _address;
    // TODO: see if its possible to replace with buffStream
    private NetworkStream _stream;
    private readonly int _port;
    private const int RETRIES = 5;

    public ABXExchangeServerClient(string address, int port)
    {
        _address = address;
        _port = port;
        _stream = GetNewStream();
    }

    private NetworkStream GetNewStream()
    {

        var tcp = new TcpClient();
        tcp.Connect(_address, _port);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 500;
        Console.WriteLine("connected now");
        _tcp = tcp;
        return stream;
    }

    private void Reconnect()
    {
        _stream = GetNewStream();
    }


    private void MakeSureTCPConnectionIsConnected()
    {
        // if (!_tcp.Client.Connected)
        // {
        // Console.WriteLine(">>>>>> tcp is not connected");
        // _tcp = new TcpClient();
        // _tcp.Connect(_address, _port);
        // Console.WriteLine($"now is it connected, {_tcp.Connected}");
        // _stream = _tcp.GetStream();
        // _stream.ReadTimeout = 500;
        // }
        // else
        // {
        //     Console.WriteLine(">>>>>> tcp is already connected");
        // }
    }

    public int Read(Span<byte> buf)
    {
        var retryNum = 0;
        while (retryNum < ABXExchangeServerClient.RETRIES)
        {
            try
            {
                return _stream.Read(buf);
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
        return 0;
        // catch (Exception e)
        // {
        //     Console.WriteLine("......caught Exception in client ");
        //     throw e;
        // }
    }

    public void WriteRequest(ABXRequest request)
    {
        // MakeSureTCPConnectionIsConnected();
        request.WriteToSTream(_stream);
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
