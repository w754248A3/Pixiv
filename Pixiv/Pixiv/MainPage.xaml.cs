﻿using LeiKaiFeng.Http;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace Pixiv
{
    static class Log
    {
        static readonly object s_lock= new object();

        static void Write_(string name, object obj)
        {
            lock (s_lock)
            {
                string s = System.Environment.NewLine;

                File.AppendAllText($"/storage/emulated/0/pixiv.{name}.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{obj}", System.Text.Encoding.UTF8);
            }
        }

        public static void Write(string name, object obj)
        {
            Write_(name, obj);
        }

        public static void Write(string name, Task task)
        {
            task.ContinueWith((t) =>
            {
                try
                {
                    t.Wait();
                }
                catch(Exception e)
                {
                    Log.Write(name, e);
                }
            });
        }
    }

    static class CreatePixivData
    {
        const string ORIGINAL_BASE_PATH = "https://i.pximg.net/img-original/img/";

        static readonly Regex s_re_original = new Regex(@"""original"":""([^""]+)""");

        //Regex m_re_small = new Regex(@"""small"":""([^""]+)""");

        static readonly Regex s_re_mark = new Regex(@"""bookmarkCount"":(\d+),");

        static readonly Regex s_re_tags = new Regex(@"""tags"":\[([^\]]+)\],""userId""");



        static string F(Regex re, string html)
        {
            Match match = re.Match(html);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                throw new FormatException();
            }
        }

        static string GetPath(string s)
        {

            return s.Substring(ORIGINAL_BASE_PATH.Length);
        }

        public static PixivData Create(int itemId, string html)
        {

            int mark = int.Parse(F(s_re_mark, html));

            string path = GetPath(F(s_re_original, html));

            string tags = F(s_re_tags, html);

            return new PixivData(itemId, mark, path, tags);
        }

        public static Uri GetOriginalUri(string path)
        {
            return new Uri(ORIGINAL_BASE_PATH + path);
        }

        static string WithOut(string s)
        {
            int index = s.IndexOf(".", StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                return s;
            }
            else
            {

                return s.Substring(0, index);
            }

        }

        public static Uri GetSmallUri(PixivData data)
        {
            return new Uri("https://i.pximg.net/c/540x540_70/img-master/img/" + WithOut(data.Path) + "_master1200.jpg");
        }


        public static Uri GetNextUri(int value)
        {
            return new Uri("https://www.pixiv.net/artworks/" + value);
        }

    }

    static class CreatePixivMHttpClient
    {
        const string HOST = "www.pixivision.net";

        static Task CreateConnectAsync(Socket socket, Uri uri)
        {
            return socket.ConnectAsync(HOST, 443);

            //while (true)
            //{
            //    try
            //    {
            //        await socket.ConnectAsync(HOST, 443).ConfigureAwait(false);

            //        return;
            //    }
            //    catch(SocketException)
            //    {
            //        //var c = e.SocketErrorCode;

            //        //if (c == SocketError.TryAgain ||
            //        //    c == SocketError.TimedOut ||
            //        //    c == SocketError.NetworkUnreachable ||
            //        //    c == SocketError.NetworkDown ||
            //        //    c == SocketError.HostUnreachable)
            //        //{
            //        //    await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
            //        //}
            //        //else
            //        //{
            //        //    throw;
            //        //}

            //        await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
            //    }
            //}


        }

        static async Task<Stream> CreateAuthenticateAsync(Stream stream, Uri uri)
        {

            SslStream sslStream = new SslStream(stream, false);

            await sslStream.AuthenticateAsClientAsync(HOST).ConfigureAwait(false);

            return sslStream;
        }

        //static async Task<T> CatchSocketExceptionAsync<T>(Func<Task<T>> func)
        //{
        //    while (true)
        //    {
        //        try
        //        {
        //            return await func().ConfigureAwait(false);
        //        }
        //        catch (MHttpClientException e)
        //        when (e.InnerException is SocketException)
        //        {
        //            await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
        //        }
        //    }


        //}

        static async Task<T> CatchOperationCanceledExceptionAsync<T>(Func<Task<T>> func, int reloadCount)
        {

            foreach (var item in Enumerable.Range(0, reloadCount))
            {
                try
                {
                    return await func().ConfigureAwait(false);
                }
                catch (MHttpClientException e)
                when (e.InnerException is OperationCanceledException)
                {
                    await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
                }
            }

            return await func().ConfigureAwait(false);
        }

        

        public static Func<Uri, Task<string>> CreateProxy(int maxStreamPoolCount, int reloadCount, TimeSpan responseTimeOut)
        {
            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = CreateConnectAsync,

                AuthenticateCallback = CreateAuthenticateAsync,

                MaxStreamPoolCount = maxStreamPoolCount


            });


            client.ResponseTimeOut = responseTimeOut;

            return (uri) =>
            {
                return CatchOperationCanceledExceptionAsync(() => client.GetStringAsync(uri), reloadCount);
            };
        }

        public static Func<Uri, Uri, Task<byte[]>> Create(int maxStreamPoolCount, int maxResponseSize, int reloadCount, TimeSpan responseTimeOut)
        {
            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                MaxStreamPoolCount = maxStreamPoolCount,
                MaxResponseSize = maxResponseSize
                
            });

            client.ResponseTimeOut = responseTimeOut;

            return (uri, referer) =>
            {
                return CatchOperationCanceledExceptionAsync(() => client.GetByteArrayAsync(uri, referer), reloadCount);
            };
        }
    }


    public sealed class PixivData
    {
        public PixivData()
        {
        }

        public PixivData(int itemId, int mark, string path, string tags)
        {
            ItemId = itemId;

            Mark = mark;

            Path = path;

            Tags = tags;
        }

        [PrimaryKey]
        public int ItemId { get; set; }


        public int Mark { get; set; }

        [MaxLength(40)]
        public string Path { get; set; }

        [MaxLength(200)]
        public string Tags { get; set; }
    }

    static class DataBase
    {
        const int START_VALUE = 60000000;

        const string DatabaseFilename = "PixivBaseData.db3";

        const SQLite.SQLiteOpenFlags Flags =

            SQLite.SQLiteOpenFlags.ReadWrite |

            // create the database if it doesn't exist
            SQLite.SQLiteOpenFlags.Create |
            // enable multi-threaded database access
            SQLite.SQLiteOpenFlags.SharedCache |

            SQLite.SQLiteOpenFlags.FullMutex;

        static SQLiteConnection s_connection;

        static SemaphoreSlim s_slim;

        static string s_basePath;

        static SQLiteConnection Create(string path)
        {
            string s = Path.Combine(path, DatabaseFilename);

            var conn = new SQLiteConnection(s, Flags);

            conn.CreateTable<PixivData>();

            return conn;
        }

        public static void Init(string basePath)
        {
            Directory.CreateDirectory(basePath);

            s_basePath = basePath;

            s_connection = Create(s_basePath);

            s_slim = new SemaphoreSlim(1, 1);
        }

        static int GetMaxItemId_()
        {
           
            var datas = s_connection.Table<PixivData>().OrderByDescending((v) => v.ItemId).Take(1).ToList();

            if (datas.Count == 0)
            {
                return START_VALUE;
            }
            else
            {
                return datas[0].ItemId;
            }

        }

        static int Add_(PixivData data)
        {
            return s_connection.InsertOrReplace(data);
        }

        static int AddAll_(List<PixivData> datas)
        {
            foreach (var item in datas)
            {
                Add_(item);
            }

            return default;
        }

        static PixivData Find_(int id)
        {
            return s_connection.Find<PixivData>(id);
        }

        static List<int> FindNotHaveId_(int start, int count)
        {
            return Enumerable.Range(start, count)
                .Where((n) => Find_(n) is null)
                .ToList();
        }

        static List<int> FindHaveId_(int start, int count)
        {

            return Enumerable.Range(start, count)
                .Where((n) => !(Find_(n) is null))
                .ToList();

        }

        static List<PixivData> Select_((int minId, int maxId)? idSpan, int minMark, int maxMark, string tag, string notTag, int offset, int count)
        {
            
            var query = s_connection.Table<PixivData>();

            query = query.Where((v) => v.Mark >= minMark && v.Mark <= maxMark);

            if (idSpan.HasValue)
            {
                var (minId, maxId) = idSpan.Value;

                query = query.Where((v) => v.ItemId >= minId && v.ItemId <= maxId);

            }

            if (string.IsNullOrWhiteSpace(tag) == false)
            {
                query = query.Where((v) => v.Tags.Contains(tag));
            }


            if(string.IsNullOrWhiteSpace(notTag) == false)
            {
                query = query.Where((v) => !v.Tags.Contains(notTag));
            }

            query = query.OrderByDescending((v) => v.Mark);

            return query.Skip(offset).Take(count).ToList();

        }

        static Task<T> F<T>(Func<T> func)
        {
            return Task.Run(() => FF(func));
        }

        static async Task<T> FF<T>(Func<T> func)
        {

            try
            {
                await s_slim.WaitAsync().ConfigureAwait(false);


                return func();
            }
            finally
            {
                s_slim.Release();
            }

        }

        public static Task<List<PixivData>> Select((int minId, int maxId)? idSpan, int minMark, int maxMark, string tag, string notTag, int offset, int count)
        {
            return F(() => Select_(idSpan, minMark, maxMark, tag, notTag, offset, count));
        }


        public static Task<int> GetMaxItemId()
        {
            return F(GetMaxItemId_);
        }

        public static Task<int> Add(PixivData data)
        {
            return F(() => Add_(data));
        }

        public static Task<PixivData> Find(int id)
        {
            return F(() => Find_(id));
        }

        public static Task<List<int>> FindNotHaveId(int start, int count)
        {
            return F(() => FindNotHaveId_(start, count));
        }

        public static Task<List<int>> FindHaveId(int start, int count)
        {
            return F(() => FindHaveId_(start, count));
        }

        public static Task AddAll(List<PixivData> datas)
        {
            return F(() => AddAll_(datas));
        }
    }


    sealed class Crawling
    {

       

        sealed class CrawlingCount
        {
            readonly int m_max_ex_count;

            volatile int m_id;

            volatile int m_ex_count;

            volatile int m_timeOut_count = 0;

            volatile int m_response_exception_count = 0;

            volatile int m_start_count = 0;

            public Func<int, Task<bool>> SelectIdFunc { get; set; }

            public Func<bool> IsAddResponseExFunc { get; set; }

            static int InitId(int id)
            {
                if (id < 0)
                {
                    return -1;
                }
                else
                {
                    id -= 1;

                    if (id < 0)
                    {
                        return -1;
                    }
                    else
                    {
                        return id;
                    }
                }

            }

            public CrawlingCount(int max_ex_count, int id)
            {
                m_max_ex_count = max_ex_count;

                m_id = InitId(id);

                m_ex_count = 0;

                SelectIdFunc = (n) => Task.FromResult(true);

                IsAddResponseExFunc = () => true;
            }


            public int Id()
            {
                return m_id;
            }

            public async Task<int> GetNextId()
            {
                while (true)
                {
                    int n = Interlocked.Increment(ref m_id);



                    if (n < 0)
                    {
                        throw new ArgumentOutOfRangeException();
                    }
                    else
                    {
                        bool b = await SelectIdFunc(n).ConfigureAwait(false);

                        if (b)
                        {
                            return n;
                        }
                    }
                }

                
            }

            public int GetStartCount()
            {
                return m_start_count;
            }

            public void AddStartCount()
            {
                Interlocked.Increment(ref m_start_count);
            }

            public void Completed()
            {

                m_ex_count = 0;

            }

            public int GetResponseCount()
            {
                return m_response_exception_count;
            }

            void AddResponseCount()
            {
                Interlocked.Increment(ref m_response_exception_count);
            }

            public bool IsReturn()
            {
                AddResponseCount();

                if (IsAddResponseExFunc() == false)
                {
                    return false;
                }

                if (Interlocked.Increment(ref m_ex_count) >= m_max_ex_count)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public int GetTimeOutCount()
            {
                return m_timeOut_count;
            }

            public void AddTimeOut()
            {
                Interlocked.Increment(ref m_timeOut_count);
            }
        }


        readonly Func<Uri, Task<string>> m_client;

        readonly CrawlingCount m_count;

        public Task Task { get; private set; }

        public int Id => m_count.Id();

        public string Message => $"Id:{Id} C:{Task.IsCompleted} T:{m_count.GetTimeOutCount()} S:{m_count.GetStartCount()} R:{m_count.GetResponseCount()}";

        private Crawling(int startId, int runCount, int maxExCount, int reloadCount, TimeSpan responseTimeOut)
        {

            m_client = CreatePixivMHttpClient.CreateProxy(runCount, reloadCount, responseTimeOut);

            m_count = new CrawlingCount(maxExCount, startId);

        }


        async Task WhileAsync()
        {
            while (true)
            {

                try
                {
                    int n;

                    try
                    {

                        n = await m_count.GetNextId().ConfigureAwait(false);

                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return;
                    }

                    Uri uri = CreatePixivData.GetNextUri(n);

                    m_count.AddStartCount();

                    string html = await m_client(uri).ConfigureAwait(false);

                    m_count.Completed();

                    PixivData data = CreatePixivData.Create(n, html);

                    await DataBase.Add(data).ConfigureAwait(false);
                }
                catch (MHttpClientException e)
                when (e.InnerException is MHttpResponseException)
                {
                    if (m_count.IsReturn())
                    {
                        return;
                    }
                }
                catch(MHttpClientException e)
                when(e.InnerException is OperationCanceledException)
                {
                    m_count.AddTimeOut();
                }
                catch(Exception e)
                {
                    Log.Write("crawling", e);

                }
            }
        }

        public static Crawling Start(bool isOnlyCrawlingNotSave, int? endId, int startId, int runCount, int maxExCount, int reloadCount, TimeSpan responseTimeOut)
        {
            Crawling crawling = new Crawling(startId, runCount, maxExCount, reloadCount, responseTimeOut);

            Func<int, Task<bool>> func = (n) => Task.FromResult(true);


            if (isOnlyCrawlingNotSave)
            {
                func = async (n) =>
                {
                    var item = await DataBase.Find(n).ConfigureAwait(false);

                    if (item is null)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                };



                crawling.m_count.IsAddResponseExFunc = () => false;
            }


            if (endId.HasValue)
            {

                var f = func;

                func = async (n) =>
                {
                    if (n <= endId)
                    {
                        return await f(n).ConfigureAwait(false);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException();
                    }
                    
                };

            }

            crawling.m_count.SelectIdFunc = func;

            crawling.Task = Task.Run(() =>
            {
                List<Task> tasks = new List<Task>();

                foreach (var item in Enumerable.Range(0, runCount))
                {
                    tasks.Add(crawling.WhileAsync());
                }


                return Task.WhenAll(tasks.ToArray());
            });

            Log.Write("crawling", crawling.Task);

            return crawling;
        }
    }

    sealed class Crawling2
    {
        public enum Mode
        {
            All,
            OnlyNotHave,
            OnlyHave
        }

        static async Task PreLoadIdTask(MyChannels<int> channelsId, int startId, int count, Func<int, int, Task<List<int>>> getList, Func<int, bool> isEndId)
        {
            startId = InitId(startId);

            try
            {

                while (true)
                {
                    var list = await getList(startId, count).ConfigureAwait(false);

                    foreach (var item in list)
                    {
                        if (isEndId(item))
                        {
                            return;
                        }
                        else
                        {
                            await channelsId.WriteAsync(item).ConfigureAwait(false);
                        }
                    }

                    startId += count;
                }
            }
            catch (MyChannelsCompletedException)
            {

            }

        }

        static void De()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {

                var v = Xamarin.Essentials.Connectivity.NetworkAccess;

                Log.Write("net", v);
            });

            Thread.Sleep(new TimeSpan(0, 0, 2));
        }

        static async Task LoadHtmlLoopTask(Func<Uri, Task<string>> func, MyChannels<int> channelsId, MyChannels<PixivData> channelsData, CountPack countPack, Func<Exception, bool> isCatch)
        {
            while (true)
            {
                try
                {
                    int n = await channelsId.ReadAsync().ConfigureAwait(false);

                    Uri uri = CreatePixivData.GetNextUri(n);


                    countPack.Id = n;

                    countPack.Load++;

                    string html = await func(uri).ConfigureAwait(false);


                    PixivData data = CreatePixivData.Create(n, html);

                    await channelsData.WriteAsync(data).ConfigureAwait(false);
                }
                catch (MyChannelsCompletedException)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (isCatch(e))
                    {


                    }
                    else
                    {
                        throw;
                    }

                }

            }

        }

        static async Task SaveTask(MyChannels<PixivData> channelsData, int maxCount, CountPack countPack)
        {
            TimeSpan timeSpan = new TimeSpan(0, 5, 0);

            Task timeOutTask = Task.Delay(timeSpan);

            Func<Task, Task<bool>> isTimeOut = async (itemTask) =>
            {
                var t = await Task.WhenAny(itemTask, timeOutTask).ConfigureAwait(false);

                if (object.ReferenceEquals(t, itemTask))
                {
                    return false;
                }
                else
                {
                    timeOutTask = Task.Delay(timeSpan);

                    return true;
                }
            };
            
            var list = new List<PixivData>(maxCount);

            Func<Task> addAll = () =>
            {
                var v = list;

                list = new List<PixivData>(maxCount);

                countPack.Save += v.Count;

                return DataBase.AddAll(v);

            };

            Func<Task<PixivData>, Task> addAllOver = async (t) =>
            {
                var item = await t.ConfigureAwait(false);

                list.Add(item);


                if (list.Count >= maxCount)
                {
                    await addAll().ConfigureAwait(false);
                }
                
            };

            Func<Task> addAllTimeOut = () =>
            {
                if (list.Count != 0)
                {
                    return addAll();
                }
                else
                {
                    return Task.CompletedTask;
                }
            };

            try
            {

                while (true)
                {
                    var itemTask = channelsData.ReadAsync();

                    if (itemTask.IsCompleted)
                    {
                        await addAllOver(itemTask).ConfigureAwait(false);
                    }
                    else
                    {

                        if (await isTimeOut(itemTask).ConfigureAwait(false))
                        {
                            await addAllTimeOut().ConfigureAwait(false);
                   
                            await addAllOver(itemTask).ConfigureAwait(false);
                        }
                        else
                        {
                            await addAllOver(itemTask).ConfigureAwait(false);
                        }
                        
                    }

                }
            }
            catch (MyChannelsCompletedException)
            {

            }

            await DataBase.AddAll(list).ConfigureAwait(false);
        }

        static Func<Uri, Task<T>> CatchMHttpResponseExceptionAsync<T>(Func<Uri, Task<T>> func, int maxExCount)
        {
            int count = 0;

            return async (uri) =>
            {
                try
                {

                    var v = await func(uri).ConfigureAwait(false);

                    count = 0;

                    return v;
                }
                catch (MHttpClientException e)
                when (e.InnerException is MHttpResponseException)
                {
                    if ((count++) >= maxExCount)
                    {
                        throw new MyChannelsCompletedException();
                    }
                    else
                    {
                        throw;
                    }
                }

            };
        }

        static Func<Uri, Task<string>> CreateClientFunc(Func<Uri, Task<string>> func, int? maxExCount)
        {
            if (maxExCount is null)
            {
                return func;
            }
            else
            {
                return CatchMHttpResponseExceptionAsync(func, maxExCount.Value);
            }
        }


        static Func<int, int, Task<List<int>>> CreateGetIdFunc(Crawling2.Mode mode)
        {
            if (mode == Mode.OnlyNotHave)
            {
                return (start, count) => DataBase.FindNotHaveId(start, count);
            }
            else if(mode == Mode.All)
            {
                return (start, count) => Task.FromResult(Enumerable.Range(start, count).ToList());
            }
            else
            {
                return (start, count) => DataBase.FindHaveId(start, count);
            }
        }

        static Func<int, bool> CreateEndFunc(int? endId)
        {

            if (endId is null)
            {
                return (n) => false;
            }
            else
            {
                int v = endId.Value;

                return (n) => n > v;
            }
        }

        static int InitId(int id)
        {
            if (id < 0)
            {
                return -1;
            }
            else
            {
                id -= 1;

                if (id < 0)
                {
                    return -1;
                }
                else
                {
                    return id;
                }
            }

        }

        static Func<Exception, bool> CreateIsCatchFunc(CountPack countPack)
        {
            return (e) =>
            {
                if (e is MHttpClientException mhce)
                {

                    var ee = mhce.InnerException;

                    if (ee is MHttpResponseException)
                    {
                        countPack.Res404++;
                    }
                    else if (ee is OperationCanceledException)
                    {
                        countPack.TimeOut++;
                    }
                    else if (ee is SocketException)
                    {
                        De();
                    }
                    else if (ee is IOException ioe)
                    {
                        if (ioe.InnerException is SocketException)
                        {
                            De();
                        }
                    }
                    else
                    {
                        Log.Write("cload", mhce);
                    }

                    return true;
                }
                else
                {
                    return false;
                }


            };
        }

        public static Crawling2 Start(Crawling2.Mode mode, int? maxExCount, int startId, int? endId, int runCount, int reloadCount, TimeSpan responseTimeOut)
        {
           

            const int ID_PRE_LOAD_COUNT = 1000;

            startId -= ID_PRE_LOAD_COUNT * 4;

            var countPack = new CountPack();

            var clientFunc = CreateClientFunc(
                CreatePixivMHttpClient.CreateProxy(runCount, reloadCount, responseTimeOut),
                maxExCount);

            var ids = new MyChannels<int>(ID_PRE_LOAD_COUNT);

            var datas = new MyChannels<PixivData>(ID_PRE_LOAD_COUNT);

            var getListFunc = CreateGetIdFunc(mode);

            var getIsendFunc = CreateEndFunc(endId);

            var t1 = Task.Run(() => PreLoadIdTask(ids, startId, ID_PRE_LOAD_COUNT, getListFunc, getIsendFunc));
            
            t1.ContinueWith((t) => ids.CompleteAdding());

            Log.Write("c2", t1);

            var t2 = Task.Run(() => SaveTask(datas, ID_PRE_LOAD_COUNT, countPack));
           
            t2.ContinueWith((t) => datas.CompleteAdding());

            Log.Write("c2", t2);

            var ts = new List<Task>();

            var isCatch = CreateIsCatchFunc(countPack);

            foreach (var item in Enumerable.Range(0, runCount))
            {
                ts.Add(Task.Run(() => LoadHtmlLoopTask(clientFunc, ids, datas, countPack, isCatch)));
            }

            var craw = new Crawling2();

            craw.Task = Task.WhenAll(ts.ToArray());
            
            craw.Task.ContinueWith((t) =>
                {
                    ids.CompleteAdding();
                    datas.CompleteAdding();
                });

            Log.Write("c2", craw.Task);

            craw.Count = countPack;

            return craw;
        }

        sealed class CountPack
        {

            public int Id { get; set; }

            public int Save { get; set; }

            public int Res404 { get; set; }

            public int TimeOut { get; set; }

            public int Load { get; set; }
        }


        private CountPack Count { get; set; }

        public Task Task { get; private set; }

        public int Id => Count.Id;


        public string Message => $"ID:{Count.Id} L:{Count.Load} S:{Count.Save} R:{Count.Res404} T:{Count.TimeOut} C:{Task.IsCompleted}";

        private Crawling2()
        {

        }
    }

    sealed class LoadBigImg
    {

        sealed class LoadBigImgCount
        {
            volatile int m_count;

            public int Count => m_count;

            public LoadBigImgCount()
            {
                m_count = 0;
            }

            public void AddCount()
            {
                Interlocked.Increment(ref m_count);
            }


            public void SubCount()
            {
                Interlocked.Decrement(ref m_count);
            }
        }

        readonly LoadBigImgCount m_count = new LoadBigImgCount();

        readonly Func<Uri, Uri, Task<byte[]>> m_client;

        readonly string m_basePath;

        public int Count => m_count.Count;

        public LoadBigImg(string basePath, int bigImgResponseSize, int reLoadCount, TimeSpan responseTimeOut)
        {
            
            Directory.CreateDirectory(basePath);

            m_basePath = basePath;


            m_client = CreatePixivMHttpClient.Create(6, bigImgResponseSize, reLoadCount, responseTimeOut);
        }

        async Task SaveImage(byte[] buffer)
        {
            string name = Path.Combine(m_basePath, Path.GetRandomFileName() + ".png");

            using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
            {

                await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
        }

        async Task Load(string path)
        {
            Uri uri = CreatePixivData.GetOriginalUri(path);


            Uri referer = new Uri("https://www.pixiv.net/");


            byte[] buffer = await m_client(uri, referer).ConfigureAwait(false);

            await SaveImage(buffer).ConfigureAwait(false);

        }

        async Task LoadCount(string path)
        {
            try
            {
                m_count.AddCount();

                await Load(path).ConfigureAwait(false);
            }
            finally
            {
                m_count.SubCount();
            }
        }

        public void Add(string path)
        {
            Task.Run(() => LoadCount(path));
        }
    }

    sealed class Preload
    {
        readonly MyChannels<Data> m_channels;

        readonly Action m_Complete;

        private Preload(MyChannels<Data> channels, Action complete)
        {
            m_channels = channels;
            m_Complete = complete;
        }

        public Task<Data> ReadAsync()
        {
            return Task.Run(m_channels.ReadReportCompletedImmediatelyAsync);
        } 


        public void Complete()
        {


            m_Complete();

            m_channels.CompleteAdding();

        }

        static Task<byte[]> GetImageFromWebAsync(Func<Uri, Uri, Task<byte[]>> func, PixivData data)
        {
            Uri uri = CreatePixivData.GetSmallUri(data);


            Uri referer = new Uri("https://www.pixiv.net/");


            return func(uri, referer);
        }

        static async Task CreateLoadImg(Func<Uri, Uri, Task<byte[]>> func, MyChannels<PixivData> source, MyChannels<Data> destion)
        {
            while (true)
            {
                PixivData data;

                try
                {
                    data = await source.ReadAsync().ConfigureAwait(false);
                }
                catch (MyChannelsCompletedException)
                {
                    return;
                }

                byte[] buffer;
                
                try
                {
                    buffer = await GetImageFromWebAsync(func, data).ConfigureAwait(false);
                }
                catch (MHttpClientException)
                {
                    continue;
                }

                var item = new Data(buffer, data.Path, data.ItemId, data.Tags);

                try
                {
                    await destion.WriteAsync(item).ConfigureAwait(false);
                }
                catch (MyChannelsCompletedException)
                {
                    return;
                }
                
            }
        }

        static async Task CreateLoadData(MyChannels<PixivData> destion, Func<Task<List<PixivData>>> func)
        {
            while (true)
            {
                var list = await func().ConfigureAwait(false);

                if (list.Count == 0)
                {
                    return;
                }

                try
                {

                    foreach (var item in list)
                    {
                        await destion.WriteAsync(item).ConfigureAwait(false);
                    }
                }
                catch (MyChannelsCompletedException)
                {
                    return;
                }

            }
        }


        public static Preload Create(Func<Task<List<PixivData>>> func, int dataLoadCount, int imgLoadCount, int responseSize, int reLoadCount, TimeSpan responseTimeOut)
        {

            var datas = new MyChannels<PixivData>(dataLoadCount);

            var imgs = new MyChannels<Data>(imgLoadCount);

            var client = CreatePixivMHttpClient.Create(imgLoadCount, responseSize, reLoadCount, responseTimeOut);


            var t1 = Task.Run(() => CreateLoadData(datas, func));
           
            t1.ContinueWith((t) => datas.CompleteAdding());

            Log.Write("preload", t1);

            var list = new List<Task>();

            foreach (var item in Enumerable.Range(0, imgLoadCount)) 
            {
                Task t = Task.Run(() => CreateLoadImg(client, datas, imgs));

                list.Add(t);
            }


            var t2 = Task.WhenAll(list.ToArray());
          
            t2.ContinueWith((t) => imgs.CompleteAdding());

            Log.Write("preload", t2);

            return new Preload(imgs, datas.CompleteAdding);
        }
    }


    public sealed class Data
    {
        public Data(byte[] buffer, string path, int id, string tags)
        {
            Id = id;

            Path = path;

            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));

            Tags = tags;
        }

        public int Id { get; }

        public string Path { get; }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }

        public string Tags { get; }
    }

    sealed class Xml
    {

        public static string[] GetDeserializeXml(string xml)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(string[]));



            return (string[])serializer.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(xml)));


        }


        public static string GetSerializeXml(string[] ss)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(string[]));


            MemoryStream memoryStream = new MemoryStream();


            serializer.Serialize(memoryStream, ss);



            return Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, checked((int)memoryStream.Position));
        }


    }

    static class InputData
    {
        public static int EndId
        {
            get => Preferences.Get(nameof(EndId), 86000201);

            set => Preferences.Set(nameof(EndId), value);
        }

        public static int TaskCount
        {
            get => Preferences.Get(nameof(TaskCount), 64);

            set => Preferences.Set(nameof(TaskCount), value);
        }

        public static int Id
        {
            get => Preferences.Get(nameof(Id), 66000201);

            set => Preferences.Set(nameof(Id), value);
        }

        public static string MinId
        {
            get => Preferences.Get(nameof(MinId), "80000201");

            set => Preferences.Set(nameof(MinId), value);
        }


        public static string MaxId
        {
            get => Preferences.Get(nameof(MaxId), "88000201");

            set => Preferences.Set(nameof(MaxId), value);
        }

        public static int Min
        {
            get => Preferences.Get(nameof(Min), 0);

            set=> Preferences.Set(nameof(Min), value);
        }

        public static int Max
        {
            get => Preferences.Get(nameof(Max), 10000);

            set => Preferences.Set(nameof(Max), value);
        }

        public static string NotTag
        {
            get => Preferences.Get(nameof(NotTag), "漫画");

            set => Preferences.Set(nameof(NotTag), value);
        }

        public static string Info
        {
            get => Preferences.Get(nameof(Info), "");

            set => Preferences.Set(nameof(Info), value);
        }

        public static string Tag
        {
            get => Preferences.Get(nameof(Tag), "");

            set => Preferences.Set(nameof(Tag), value);
        }

        public static int Offset
        {
            get => Preferences.Get(nameof(Offset), 0);

            set => Preferences.Set(nameof(Offset), value);
        }

        public static int Count
        {
            get => Preferences.Get(nameof(Count), 0);

            set => Preferences.Set(nameof(Count), value);
        }

        static int F(string s)
        {
            if (int.TryParse(s, out int n) && n >= 0)
            {
                return n;
            }
            else
            {
                throw new FormatException();
            }

        }

        static (int minId, int maxId)? CreateIdSpan(string minId, string maxId)
        {
            if(string.IsNullOrWhiteSpace(minId) ||
               string.IsNullOrWhiteSpace(maxId))
            {
                return null;
            }

            int min = F(minId);

            int max = F(maxId);

            if (min >= 0 && min < max) 
            {
                return (min, max);
            }
            else
            {
                return null;
            }
        }

        public static string[] GetInfo()
        {
            string s = InputData.Info;

            if (string.IsNullOrWhiteSpace(s))
            {
                return new string[] { };
            }
            else
            {
                return Xml.GetDeserializeXml(s);
            }
        }

        static void SetInfo(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {

            }
            else
            {
                string s = InputData.Info;

                if (string.IsNullOrWhiteSpace(s))
                {
                    InputData.Info = Xml.GetSerializeXml(new string[] { tag });
                }
                else
                {
                    var ss = Xml.GetDeserializeXml(s).Append(tag).ToArray();

                    ss = (new HashSet<string>(ss)).ToArray(); 

                    InputData.Info = Xml.GetSerializeXml(ss);
                }
            }
        }

        public static bool Create(string endId, string taskCount, string minId, string maxId, string nottag, string id, string min, string max, string tag, string offset, string count)
        {
            try
            {

                if (CreateIdSpan(minId, maxId).HasValue == false)
                {
                    return false;
                }

                MinId = minId ?? "";

                MaxId = maxId ?? "";

                EndId = F(endId);

                TaskCount = F(taskCount);

                Id = F(id);

                Min = F(min);

                Max = F(max);

                Offset = F(offset);

                Count = F(count);


                NotTag = nottag ?? "";

                Tag = tag ?? "";

                SetInfo(tag);

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static Func<Task<List<PixivData>>> CreateSelectFunc()
        {
            string nottag = NotTag;

            string tag = Tag;

            int min = Min;

            int max = Max;

            int offset = Offset;

            int count = Count;

            var idSpan = CreateIdSpan(InputData.MinId, InputData.MaxId);

            return () =>
            {
                int n = offset;

                var task = DataBase.Select(idSpan, min, max, tag, nottag, offset, count);


                offset += count;

                Offset = n;

                return task;

            };
        }
    }


    sealed class Awa
    {

        TaskCompletionSource<object> m_source;

        public void SetAwait()
        {
            if (m_source is null)
            {
                m_source = new TaskCompletionSource<object>();
            }
            else
            {

            }
        }

        public void SetAdd()
        {
            if (m_source is null)
            {

            }
            else
            {
                var v = m_source;

                m_source = null;

                v.TrySetResult(default);
            }
        }


        public Task Get()
        {
            if (m_source is null)
            {
                return Task.CompletedTask;
            }
            else
            {
                return m_source.Task;
            }
        }
    }

    public partial class MainPage : ContentPage
    {
        const int SMALL_IMG_RESPONSE_SIZE = 1024 * 1024 * 5;

        const int SMALL_IMG_RELOAD_COUNT = 3;

        const int SMALL_IMG_TIMEOUT = 30;


        const int SMALL_IMG_PERLOAD_COUNT = 12;




        const int BIG_IMG_RESPONSE_SIZE = 1024 * 1024 * 20;

        const int BIG_IMG_RELOAD_COUNT = 6;

        const int BIG_IMG_TIMEOUT = 240;





        const int CRAWLING_COUNT = 64;

        const int CRAWLING_MAX_EX_COUNT = 1000;

        const int CRAWLING_RELOAD_COUNT = 3;

        const int CRAWLING_TIMEOUT = 30;


        const int PIXIVDATA_PRELOAD_COUNT = 200;



        const int IMG_VIEW_COUNT = 400;

        const int IMG_TIMESPAN = 1;

        const int IMG_FLUSH_TIMESPAN = 6;

        const int MESSAGE_FLUSH_TIMESPAN = 5;




        const string ROOT_PATH = "/storage/emulated/0/pixiv/";

        const string BASE_PATH = ROOT_PATH + "database/";
      
        const string IMG_PATH = ROOT_PATH + "img/";

        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();

        readonly LoadBigImg m_download = new LoadBigImg(IMG_PATH, BIG_IMG_RESPONSE_SIZE, BIG_IMG_RELOAD_COUNT, new TimeSpan(0, 0, BIG_IMG_TIMEOUT));

        readonly Awa m_awa = new Awa();

        Crawling2 m_crawling;

        Action m_action;

        Task m_reloadTask;

        Preload m_reload;

        public MainPage()
        {
            InitializeComponent();


            Log.Write("Init", Init());

        }

        async Task Init()
        {
           
            var p = await Permissions.RequestAsync<Permissions.StorageWrite>();

            if (p != PermissionStatus.Granted)
            {
                Task t = DisplayAlert("错误", "需要存储权限", "确定");

                Environment.Exit(0);
                return;
            }

            DeviceDisplay.KeepScreenOn = true;


            Directory.CreateDirectory(ROOT_PATH);

            Directory.CreateDirectory(IMG_PATH);



            DataBase.Init(BASE_PATH);

            InitInputView();

            Log.Write("viewtext", InitViewText());
           

            InitCollView();
        } 

        void InitCrawlingMoedValue()
        {
            var vs = Enum.GetNames(typeof(Crawling2.Mode));

            m_crawling_moed_value.ItemsSource = vs;

            m_crawling_moed_value.SelectedIndex = 0;
        }

        void InitInputView()
        {
            InitCrawlingMoedValue();

            m_endId_value.Text = InputData.EndId.ToString();

            m_task_count_value.Text = InputData.TaskCount.ToString();

            m_tag_value_histry.ItemsSource = InputData.GetInfo();

            m_max_id_value.Text = InputData.MaxId;

            m_min_id_value.Text = InputData.MinId;

            m_nottag_value.Text = InputData.NotTag;

            m_startId_value.Text = InputData.Id.ToString();

            m_tag_value.Text = InputData.Tag;

            m_min_value.Text = InputData.Min.ToString();
           
            m_max_value.Text = InputData.Max.ToString();
            
            m_offset_value.Text = InputData.Offset.ToString();
           
        }

        bool CreateInput()
        {
            return InputData.Create(
                m_endId_value.Text,
                m_task_count_value.Text,
                m_min_id_value.Text,
                m_max_id_value.Text,
                m_nottag_value.Text,
                m_startId_value.Text,
                m_min_value.Text,
                m_max_value.Text,
                m_tag_value.Text,
                m_offset_value.Text,
                PIXIVDATA_PRELOAD_COUNT.ToString());
        }


        async Task InitViewText()
        {
            while (true)
            {
                string s = "";
               
                if (m_crawling is null)
                {
                    
                }
                else
                {
                    s += $"{m_crawling.Message}";
                }

                s += $" D:{m_download.Count}";

                if(m_reloadTask is null)
                {

                }
                else
                {
                    s += $" V:{m_reloadTask.IsCompleted}";
                }

                m_viewText.Text = s;

                if (m_action != null)
                {
                    m_action();
                }

                await Task.Delay(new TimeSpan(0, 0, MESSAGE_FLUSH_TIMESPAN));
            }
        } 

        void InitCollView()
        {
            m_collView.ItemsSource = m_imageSources;
        }

        void OnStart(object sender, EventArgs e)
        {
            
            if (CreateInput() == false)
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }
            else
            {
                m_cons.IsVisible = false;

                m_reload = Preload.Create(InputData.CreateSelectFunc(), PIXIVDATA_PRELOAD_COUNT, SMALL_IMG_PERLOAD_COUNT, SMALL_IMG_RESPONSE_SIZE, SMALL_IMG_RELOAD_COUNT, new TimeSpan(0, 0, SMALL_IMG_TIMEOUT));

                m_reloadTask = Start(m_reload);

                Log.Write("reload", m_reloadTask);
            }
        }

        async Task SetImage(Data date)
        {

            if (m_imageSources.Count >= IMG_VIEW_COUNT)
            {
                await Task.Delay(new TimeSpan(0, 0, IMG_FLUSH_TIMESPAN));

                m_imageSources.Clear();

                await Task.Yield();
            }

            m_imageSources.Add(date);

            await Task.Yield();

            m_collView.ScrollTo(m_imageSources.Count - 1, position: ScrollToPosition.End, animate: false);

            await Task.Yield();
        }

        

        async Task Start(Preload reload)
        {

            try
            {
                while (true)
                {
                    await m_awa.Get();

                    var data = await reload.ReadAsync();

                    await SetImage(data);

                    await Task.Delay(new TimeSpan(0, 0, IMG_TIMESPAN));
                }
            }
            catch (MyChannelsCompletedException)
            {
                
            }
           
             
        }

        protected override bool OnBackButtonPressed()
        {
            if (m_reload is null || m_reloadTask is null)
            {

            }
            else
            {
                m_reload.Complete();

                m_reloadTask.ContinueWith((t) =>
                {
                    MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        m_imageSources.Clear();

                        InitInputView();

                        m_cons.IsVisible = true;

                    });

                });

                m_reload = null;

                m_reloadTask = null;


                DisplayAlert("消息", "取消中", "确定");


            }

            return true;
        }


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collView.SelectedItem != null)
            {
                Data data = (Data)m_collView.SelectedItem;

                Clipboard.SetTextAsync(CreatePixivData.GetNextUri(data.Id).AbsoluteUri + "    " + data.Tags);

                m_download.Add(data.Path);

                m_collView.SelectedItem = null;
            }
        }

        void OnScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            long n = (long)e.VerticalDelta;

            if (n != 0)
            {
                if (n < 0)
                {
                    m_awa.SetAwait();
                }
                else if (n > 0 && e.LastVisibleItemIndex + 1 == m_imageSources.Count)
                {
                    m_awa.SetAdd();
                }
            }
        }

        void OnStartFromLastId(object sender, EventArgs e)
        {
            m_start_cons.IsVisible = false;

            MainThread.InvokeOnMainThreadAsync(async () =>
            {
                int id = await DataBase.GetMaxItemId();

                m_crawling = Crawling2.Start(Crawling2.Mode.All, CRAWLING_MAX_EX_COUNT, id, null, CRAWLING_COUNT, CRAWLING_RELOAD_COUNT, new TimeSpan(0, 0, CRAWLING_TIMEOUT));

            });
        }

        static Crawling2.Mode GetCrawlingMoedValue(string s)
        {
            return (Crawling2.Mode)Enum.Parse(typeof(Crawling2.Mode), s);

        }

        void OnStartFromInputId(object sender, EventArgs e)
        {
            if (CreateInput())
            {
                m_start_cons.IsVisible = false;

                MainThread.InvokeOnMainThreadAsync(() =>
                {
                    int id = InputData.Id;

                    int endId = InputData.EndId;

                    int taskCount = InputData.TaskCount;

                    var moed = GetCrawlingMoedValue(m_crawling_moed_value.SelectedItem.ToString());

                    m_crawling = Crawling2.Start(moed, null, id, endId, taskCount, CRAWLING_RELOAD_COUNT, new TimeSpan(0, 0, CRAWLING_TIMEOUT));


                    m_action = () => InputData.Id = m_crawling.Id;
                });
            }
            else
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }  
        }

        void OnTagEntryFocused(object sender, FocusEventArgs e)
        {
            m_tag_value_histry.IsVisible = true;

            m_nottag_cons.IsVisible = false;
        }

        void OnTagEntryUnFocused(object sender, FocusEventArgs e)
        {
            m_tag_value_histry.IsVisible = false;

            m_nottag_cons.IsVisible = true;
        }

        void OnTagHistrySelect(object sender, SelectionChangedEventArgs e)
        {
            m_tag_value.Text = m_tag_value_histry.SelectedItem.ToString();

            m_tag_value_histry.IsVisible = false;

            m_nottag_cons.IsVisible = true;
        }

        void OnVisibleStartConsole(object sender, EventArgs e)
        {
            m_start_cons.IsVisible = false;
        }
    }
}