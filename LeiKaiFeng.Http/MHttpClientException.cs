using System;

namespace LeiKaiFeng.Http
{
    [Serializable]
    public sealed class MHttpClientException : Exception
    {
        public MHttpClientException(Exception e) : base(string.Empty, e)
        {

        }
    }
}