// This file encapsulates all uses of the Squared APIs.
// TODO: Remove the use of Squared.Threading and Squared.Util in this namespace.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Threading;

namespace crdebug {
    public static class Util {
        public static Future<T> IncompleteFuture<T> () {
            return new Future<T>();
        }
    }
}
