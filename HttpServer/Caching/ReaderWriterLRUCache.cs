namespace HttpServer.Caching;

internal class ReaderWriterLRUCache<K, T>
    where K : notnull
    where T : notnull
{
    private Dictionary<K, T> cache;
    private ReaderWriterLockSlim cacheLock;
    private LinkedList<K> lruList;

    int capacity;
    int Capacity => capacity;

    public ReaderWriterLRUCache(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be greater than 0.");
        }
        this.capacity = capacity;
        cache = new Dictionary<K, T>(capacity);
        cacheLock = new ReaderWriterLockSlim();
        lruList = new LinkedList<K>();
    }

    public void Write(K key, T value)
    {
        cacheLock.EnterUpgradeableReadLock();
        try
        {
            if (cache.ContainsKey(key))
            {
                // ako kes sadrzi kljuc, samo ga premesti na pocetak liste
                // i azuriraj vrednost 
                cacheLock.EnterWriteLock();
                try
                {
                    cache[key] = value;
                    lruList.Remove(key);
                    lruList.AddFirst(key);
                }
                finally
                {
                    cacheLock.ExitWriteLock();
                }
            }
            else
            {
                cacheLock.EnterWriteLock();
                try
                {
                    // ako kes ne sadrzi kljuc, dodaj ga na pocetak liste
                    cache.Add(key, value);
                    lruList.AddFirst(key);
                    if (cache.Count > Capacity)
                    {
                        // ako je kes pun, izbaci poslednji element
                        var last = lruList.Last;
                        cache.Remove(last.Value);
                        lruList.RemoveLast();
                    }
                }
                finally
                {
                    cacheLock.ExitWriteLock();
                }
            }
        }
        finally
        {
            cacheLock.ExitUpgradeableReadLock();
        }
    }

    public bool TryRead(K key, out T readValue)
    {
        cacheLock.EnterUpgradeableReadLock();
        try
        {
            if (cache.ContainsKey(key))
            {
                // ako kes sadrzi kljuc, premesti ga na pocetak liste
                // i vrati vrednost
                cacheLock.EnterWriteLock();
                try
                {
                    readValue = cache[key];
                    lruList.Remove(key);
                    lruList.AddFirst(key);
                    return true;
                }
                finally
                {
                    cacheLock.ExitWriteLock();
                }
            }
            else
            {
                readValue = default(T)!;
                return false;
            }
        }
        finally
        {
            cacheLock.ExitUpgradeableReadLock();
        }
    }
}
