public class Result<T, E>
{
    private T _ok;
    private E _err;

    public T Ok { get { return _ok; } }
    public E Err { get { return _err; } }

    public Result(T success)
    {
        _ok = success;
        _err = default(E);
    }

    public Result(E error)
    {
        _err = error;
        _ok = default(T);
    }
}
