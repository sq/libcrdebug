using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using crdebug.RemoteTypes;

namespace crdebug.Exceptions {
    public class ChromeRemoteJSException : Exception {
        public ExceptionDetails Details;

        public ChromeRemoteJSException (ExceptionDetails details) 
            : base ((details.exception != null) ? details.exception.description ?? details.exception.className : details.text) {
            Details = details;
        }
    }

    public class ChromeRemoteException : Exception {
        public readonly int Code;

        public ChromeRemoteException (int code, string message)
            : base (message + " #" + code) {
            Code = code;
        }
    }

    public class ChromeConnectException : Exception {
        public ChromeConnectException (string message) 
            : base (message) {
        }

        public ChromeConnectException (string message, Exception innerException) 
            : base (message, innerException) {
        }
    }
}
