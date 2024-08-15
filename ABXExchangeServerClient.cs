namespace ABXConsoleClient;
using System.Net.Sockets;

public class ABXExchangeServerClient : IDisposable
{
    private TcpClient _tcp;
    private readonly string _address;
    // TODO: see if its possible to replace with buffStream
    private NetworkStream _stream;
    private readonly int _port;

    public ABXExchangeServerClient(string address, int port)
    {
        _tcp = new TcpClient();
        _address = address;
        _port = port;
    }


    private void MakeSureTCPConnectionIsConnected()
    {
        if (!_tcp.Client.Connected)
        {
            // Console.WriteLine(">>>>>> tcp is not connected");
            _tcp = new TcpClient();
            _tcp.Connect(_address, _port);
            Console.WriteLine($"now is it connected, {_tcp.Connected}");
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = 500;
        }
        // else
        // {
        //     Console.WriteLine(">>>>>> tcp is already connected");
        // }
    }

    public int Read(Span<byte> buf)
    {
        MakeSureTCPConnectionIsConnected();
        return _stream.Read(buf);
    }

    public void WriteRequest(ABXRequest request)
    {
        MakeSureTCPConnectionIsConnected();
        request.WriteToSTream(_stream);
    }

    public void Dispose()
    {
        Console.WriteLine("...dispose called");
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
