using System;

namespace LeiKaiFeng.Http
{


    [Serializable]
    public sealed class MHttpException : Exception
    {
        
        public MHttpException(string message) : base(message)
        {
            
        }
    }

}