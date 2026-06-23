using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.ObjectModel;
using System.Threading;
using System.Collections.Specialized;

namespace ThreadSafeCollection
{
    //public class ThreadSafeWrapperCollection<T> : IEnumerable<T>, INotifyCollectionChanged
    //{
    //    Dispatcher _dispatcher;
    //    ReaderWriterLock _lock;
    //    IEnumerable<T> col;
    //    public ThreadSafeWrapperCollection(INotifyCollectionChanged col)
    //    {
    //        this.col = col as IEnumerable<T>;
    //        col.CollectionChanged += col_CollectionChanged;
    //        _dispatcher = Dispatcher.CurrentDispatcher;
    //        _lock = new ReaderWriterLock();
    //    }

    //    void col_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    //    {
    //        if (_dispatcher.CheckAccess())
    //        {
    //            if (CollectionChanged != null)
    //            {
    //                LockCookie c = _lock.UpgradeToWriterLock(-1);
    //                CollectionChanged(sender, e);
    //                _lock.DowngradeFromWriterLock(ref c);
    //            }
    //        }
    //        else
    //        {
    //            _dispatcher.Invoke(DispatcherPriority.DataBind, (SendOrPostCallback)delegate {
    //                if (CollectionChanged != null)
    //                {
    //                    LockCookie c = _lock.UpgradeToWriterLock(-1);
    //                    CollectionChanged(sender, e);
    //                    _lock.DowngradeFromWriterLock(ref c);
    //                }
    //            }, null);
    //        }
    //    }
    //    public event NotifyCollectionChangedEventHandler CollectionChanged;

    //    public IEnumerator<T> GetEnumerator()
    //    {
    //        return col.GetEnumerator();
    //    }

    //    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    //    {
    //        return col.GetEnumerator();
    //    }
    //}

    public class ThreadSafeWrapperCollection<T> : IEnumerable<T>, INotifyCollectionChanged
    {
        IEnumerable<T> col;
        public ThreadSafeWrapperCollection(INotifyCollectionChanged col)
        {
            this.col = col as IEnumerable<T>;
            col.CollectionChanged += col_CollectionChanged;
        }

        void col_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (CollectionChanged != null)
                CollectionChanged(sender, e);
        }
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public IEnumerator<T> GetEnumerator()
        {
            return col.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return col.GetEnumerator();
        }
    }

}
