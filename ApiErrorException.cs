[System.Serializable]
public class ApiErrorException : System.Exception
{
    public ApiErrorException(Error error) : base(error.Message) { }
}
