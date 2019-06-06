// This file encapsulates all uses of the Squared APIs.
// TODO: Remove the use of Squared.Threading and Squared.Util in this namespace.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Squared.Threading;

namespace crdebug {
    public interface IFuture : IDisposable {
        Type ResultType { get; }
        FutureAwaitExtensionMethods.IFutureAwaiter GetAwaiter ();
        void SetResult (object value);
        void SetException (Exception exception);
        void SetException (ExceptionDispatchInfo exception);
    }

    public class Future<T> : IFuture {
        internal readonly Squared.Threading.Future<T> SquaredFuture;

        public Future () {
            SquaredFuture = new Squared.Threading.Future<T>();
        }

        public Future (T result) 
            : this () {
            SetResult(result);
        }

        public Future (Exception exception) 
            : this () {
            SetException(exception);
        }

        public Future (ExceptionDispatchInfo exception) 
            : this () {
            SetException(exception);
        }

        public void SetResult (T result) {
            SquaredFuture.SetResult2(result, null);
        }

        public void SetException (Exception exception) {
            SetException(ExceptionDispatchInfo.Capture(exception));
        }

        public void SetException (ExceptionDispatchInfo exception) {
            SquaredFuture.SetResult2(default(T), exception);
        }

        public bool TryGetResult (out T result, out Exception exception) {
            if (SquaredFuture.Failed) {
                result = default(T);
                exception = SquaredFuture.Error;
                return false;
            } else if (SquaredFuture.Completed) {
                result = SquaredFuture.Result2;
                exception = null;
                return true;
            } else {
                result = default(T);
                exception = null;
                return false;
            }
        }

        public void Cancel () {
            SquaredFuture.Dispose();
        }

        FutureAwaitExtensionMethods.IFutureAwaiter IFuture.GetAwaiter () {
            return ((Squared.Threading.IFuture)SquaredFuture).GetAwaiter();
        }

        void IFuture.SetResult (object value) {
            ((Squared.Threading.IFuture)SquaredFuture).SetResult2(value, null);
        }

        void IDisposable.Dispose () {
            SquaredFuture.Dispose();
        }

        public T Result {
            get {
                return SquaredFuture.Result2;
            }
        }

        public bool CompletedSuccessfully {
            get {
                return SquaredFuture.Completed && !SquaredFuture.Failed;
            }
        }

        public Exception Exception {
            get {
                return SquaredFuture.Error;
            }
        }

        public Type ResultType {
            get {
                return typeof(T);
            }
        }

        public FutureAwaitExtensionMethods.NonThrowingFuture<T> DoNotThrow () {
            return SquaredFuture.DoNotThrow();
        }

        // Why can't it infer this?
        public FutureAwaitExtensionMethods.FutureWithDisposedValue<T> ValueWhenDisposed (object value) {
            return SquaredFuture.ValueWhenDisposed((T)value);
        }

        public FutureAwaitExtensionMethods.FutureAwaiter<T> GetAwaiter () {
            return SquaredFuture.GetAwaiter();
        }
    }
}
