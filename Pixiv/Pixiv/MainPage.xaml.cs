using LeiKaiFeng.Http;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
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

        static Uri Get(string s, string path)
        {
            return new Uri(s + WithOut(path) + "_master1200.jpg");
        }

        public static Uri GetSmallUri(string path)
        {
            return Get("https://i.pximg.net/c/540x540_70/img-master/img/", path);
        }

        

        public static Uri GetRegularUri(string path)
        {
            return Get("https://i.pximg.net/img-master/img/", path);
        }

        public static Uri GetNextUri(int value)
        {
            return new Uri("https://www.pixiv.net/artworks/" + value);
        }

    }

    public static class ExceptionEx
    {
        public static T InnerException<T>(this Exception e) where T : Exception
        {
            if (e is null)
            {
                return null;
            }
            else
            {
                if (e.InnerException is T ee)
                {
                    return ee;
                }
                else
                {
                    return null;
                }
            }
        }

        public static bool IsNull(this Exception e)
        {
            return e is null;
        }


        public static bool IsNotNull(this Exception e)
        {
            return !IsNull(e);
        }
    }

    public enum TaskReTryFlag
    {
        None,

        Retry
    }

    sealed class TaskReTryBuild<T>
    {


        public Func<Func<CancellationToken, Task<T>>, CancellationToken, Task<T>> CreateRetryFunc(int maxTimeOutReTryCount, TimeSpan timeSpan)
        {
            Func<Task<T>, TaskReTryFlag> isRetryFunc = CreateIsRetryFunc();

            return async (func, tokan) =>
            {
                TimeSpan timeOut = default;

                foreach (var item in Enumerable.Range(0, maxTimeOutReTryCount))
                {
                    timeOut += timeSpan;

                    try
                    {
                        while (true)
                        {
                            if (tokan.IsCancellationRequested)
                            {
                                throw new OperationCanceledException();
                            }

                            using (var cancelSource = new CancellationTokenSource(timeOut))
                            using (tokan.Register(cancelSource.Cancel))
                            {
                                var task = await func(cancelSource.Token).ContinueWith((t) => t).ConfigureAwait(false);

                                var v = isRetryFunc(task);
                                if (v == TaskReTryFlag.None)
                                {
                                    return await task.ConfigureAwait(false);
                                }
                                //假如重试,则会吞噬异常,必须保证回调,能处理异常
                            }



                            await Task.Delay(new TimeSpan(0, 0, 6), tokan).ConfigureAwait(false);
                        }
                    }
                    catch (MHttpClientException e)
                    when (e.InnerException is OperationCanceledException)
                    {

                    }
                }


                throw new MHttpClientException(new OperationCanceledException());
            };
        }


        public static TaskReTryBuild<T> Create()
        {
            return new TaskReTryBuild<T>();
        }

        List<Func<Task<T>, TaskReTryFlag>> m_list = new List<Func<Task<T>, TaskReTryFlag>>();

        private TaskReTryBuild()
        {

        }


        public TaskReTryBuild<T> Add(Func<Task<T>, TaskReTryFlag> func)
        {
            m_list.Add(func);

            return this;
        }


        Func<Task<T>, TaskReTryFlag> CreateIsRetryFunc()
        {
            var vs = m_list.ToArray();

            return (task) =>
            {
                foreach (var func in vs)
                {
                    var v = func(task);
                    if (v == TaskReTryFlag.Retry)
                    {
                        return v;
                    }
                }

                return TaskReTryFlag.None;
            };
        }
    }

    public sealed class WebInfo
    {

        public string HtmlDns { get; set; }


        public string HtmlSni { get; set; }

        public string ImgDns { get; set; }


        public string ImgSni { get; set; }

    }

    public static class WebInfoHelper
    {
        public static string GetDefWebInfo()
        {
            return Ser(new WebInfo
            {
                HtmlDns = "www.pixivision.net",

                HtmlSni = "www.pixivision.net",

                ImgDns = "s.pximg.net",

                ImgSni = "s.pximg.net",

            });
        }

        public static string Ser(WebInfo info)
        {
            return JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        }


        static bool Is(string s)
        {
            try
            {
                Uri uri = new Uri("https://" + s.Trim());
                return true;
            }
            catch (UriFormatException)
            {
                return false;
            }

           
        }

        static bool Is(WebInfo info)
        {
            return Is(info.HtmlDns) &&
                    Is(info.HtmlSni) &&
                    Is(info.ImgDns) &&
                    Is(info.ImgSni);
        }

        public static WebInfo Create(string s)
        {
            return JsonSerializer.Deserialize<WebInfo>(s);
        }

        public static bool TryCreate(string s, out WebInfo info, out string errorMessage)
        {
            try
            {
                info = Create(s);

                if (Is(info))
                {

                    errorMessage = "OK";

                    return true;
                }
                else
                {
                    errorMessage = "host格式错误";

                    return false;
                }

            }
            catch (JsonException)
            {
                errorMessage = "json 错误";
            }
            catch (NotSupportedException)
            {
                errorMessage = "类型错误";
            }

            info = default;
           
            return false;

            
        }
    }

    static class CreatePixivMHttpClient
    {

        static volatile int s_read;

        static volatile int s_write;


        static volatile int s_connect;

        static volatile int s_tls;

        //public static string Message => $"Read:{s_read} Write:{s_write} Connect:{s_connect} Tls:{s_tls}";

        static void Add(ref int n)
        {
            Interlocked.Increment(ref n);
        }


        static void Sub(ref int n)
        {
            Interlocked.Decrement(ref n);
        }

        sealed class DebugStream : Stream
        {
            Stream m_stream;

            public DebugStream(Stream stream)
            {
                m_stream = stream;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Close()
            {
                m_stream.Close();
            }


            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try
                {
                    Add(ref s_write);

                    await m_stream.WriteAsync(buffer, offset, count).ConfigureAwait(false);
                }
                finally
                {
                    Sub(ref s_write);
                }
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try
                {
                    Add(ref s_read);

                    return await m_stream.ReadAsync(buffer, offset, count).ConfigureAwait(false);
                }
                finally
                {
                    Sub(ref s_read);
                }

               
            }
        }

        public static void AddReTryFunc<T>(TaskReTryBuild<T> build)
        {
            build.Add((task) =>
            {
                if (task.Exception.IsNotNull() &&
                    task.Exception.InnerException is MHttpClientException e)
                {
                    if (e.InnerException is SocketException ||
                        e.InnerException is IOException ||
                        e.InnerException is ObjectDisposedException)
                    {
                        return TaskReTryFlag.Retry;
                    }

                    return TaskReTryFlag.None;
                }
                else
                {
                    return TaskReTryFlag.None;
                }
            });
        }

        public static Func<Uri, CancellationToken, Task<string>> CreateProxy(int maxStreamPoolCount)
        {

            var webInfo = WebInfoHelper.Create(InputData.WebInfo);


            string dns_host = webInfo.HtmlDns;

            string sni_host = webInfo.HtmlSni;




            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = MHttpClientHandler.CreateCreateConnectAsyncFunc(dns_host, 443),

                AuthenticateCallback = MHttpClientHandler.CreateCreateAuthenticateAsyncFunc(sni_host),

                MaxStreamPoolCount = maxStreamPoolCount


            });

            return (uri, cancellationToken) => client.GetStringAsync(uri, cancellationToken);



        }

        public static Func<Uri, CancellationToken, Task<byte[]>> Create(int maxStreamPoolCount, int maxResponseSize)
        {
            var webInfo = WebInfoHelper.Create(InputData.WebInfo);


            string dns_host = webInfo.ImgDns;

            string sni_host = webInfo.ImgSni;



            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = MHttpClientHandler.CreateCreateConnectAsyncFunc(dns_host, 443),

                AuthenticateCallback = MHttpClientHandler.CreateCreateAuthenticateAsyncFunc(sni_host),

                MaxStreamPoolCount = maxStreamPoolCount,

                MaxResponseSize = maxResponseSize

            });

            Uri referer = new Uri("https://www.pixiv.net/");


            return (uri, tokan) => client.GetByteArrayAsync(uri, referer, tokan);

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

    public sealed class ImgData
    {
        public ImgData()
        {

        }

        public ImgData(int itemId, byte[] img)
        {
            ItemId = itemId;
            Img = img;
           
        }

        [PrimaryKey]
        public int ItemId { get; set; }

        public byte[] Img { get; set; }


        public int Count { get; set; }
    }

    sealed class ImgDataBase
    {
        public static ImgDataBase Big { get; set; }

        public static ImgDataBase Small { get; set; }

        public static void Init(string path)
        {
            Big = Create(path, "Big");

            Small = Create(path, "Small");
        }


        readonly SQLiteConnection m_connection;


        readonly SemaphoreSlim m_slim = new SemaphoreSlim(1, 1);

        private ImgDataBase(SQLiteConnection connection)
        {
            m_connection = connection;
        }

        public static ImgDataBase Create(string path, string name)
        {
            Directory.CreateDirectory(path);

            name += ".db3";

            path = Path.Combine(path, name);

            var connection = new SQLiteConnection(path, DataBase.Flags);

            connection.CreateTable<ImgData>();
            
            return new ImgDataBase(connection);
        }

        void UpCount(ImgData data)
        {
            m_connection.Execute($"UPDATE {nameof(ImgData)} SET {nameof(ImgData.Count)} = {data.Count + 1} WHERE {nameof(ImgData.ItemId)} == {data.ItemId}");
        }

        ImgData Find(int key)
        {
            var data = m_connection.Find<ImgData>(key);

            if(data is null)
            {
                return null;
            }
            else
            {
                UpCount(data);

                return data;
            }
        }


        async ValueTask<T> Lock<T>(Func<T> func)
        {
            try
            {
                await m_slim.WaitAsync().ConfigureAwait(false);

                return func();
            }
            finally
            {
                m_slim.Release();
            }
        }

        public ValueTask<int> Add(ImgData data)
        {

            return Lock(() =>
            {
                return m_connection.InsertOrReplace(data);

            });


            
        }

        public ValueTask<ImgData> Get(int itemId)
        {
            return Lock(() =>
            {
                return Find(itemId);
            });   
        }
    }

    static class DataBase
    {
        const int START_VALUE = 60000000;

        const string DatabaseFilename = "PixivBaseData.db3";

        public const SQLite.SQLiteOpenFlags Flags =

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

            //conn.CreateIndex<PixivData>((data) => data.Mark);
        
            //conn.CreateIndex<PixivData>((data) => data.Tags);

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

        public static int Vacuum(SQLiteConnection connection)
        {
            return connection.Execute("VACUUM");
        }

        public static int Delete(SQLiteConnection connection, string tableName, string itemName, int minValue)
        {
            if (minValue >= 1000)
            {
                throw new ArgumentOutOfRangeException("真的要删除吗?");
            }

            return connection.Execute($"DELETE FROM {tableName} WHERE {itemName} <= {minValue}");
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

        static List<PixivData> Select_(int? minId, int? maxId, int? minMark, int? maxMark, string tag, string notTag, int offset, int count)
        {
            
            var query = s_connection.Table<PixivData>();

            if (minMark.HasValue)
            {
                int n = minMark.Value;
                query = query.Where((v) => v.Mark >= n);

            }


            if (maxMark.HasValue)
            {
                int n = maxMark.Value;
                query = query.Where((v) => v.Mark <= n);

            }

            if (minId.HasValue)
            {
                int n = minId.Value;
                query = query.Where((v) => v.ItemId >= n);

            }


            if (maxId.HasValue)
            {
                int n = maxId.Value;
                query = query.Where((v) => v.ItemId <= n);

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

        public static Task<List<PixivData>> Select(int? minId, int? maxId, int? minMark, int? maxMark, string tag, string notTag, int offset, int count)
        {
            return F(() => Select_(minId, maxId, minMark, maxMark, tag, notTag, offset, count));
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


        public static Task<int> Delete(int minMark)
        {
            return F(() => Delete(s_connection, nameof(PixivData), nameof(PixivData.Mark), minMark));
        }

        public static Task<int> Vacuum()
        {
            return F(() => Vacuum(s_connection));
        }
    }

    sealed class Crawling
    {
        public enum Mode
        {
            All,
            OnlyNotHave,
            OnlyHave
        }

        static async Task PreLoadIdTask(ChannelWriter<int> channelsId, int startId, int count, Func<int, int, Task<List<int>>> getList, Func<int, bool> isEndId)
        { 
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
            catch (ChannelClosedException)
            {

            }

        }

        static async Task LoadHtmlLoopTask(Func<Uri, CancellationToken, Task<string>> func, CancellationToken cancellationToken, ChannelReader<int> channelId, ChannelWriter<PixivData> channelsData, Action<int> onIdAction, Action<int> endIdAction)
        {
            while (true)
            {
                try
                {
                    int n = await channelId.ReadAsync().ConfigureAwait(false);

                    Uri uri = CreatePixivData.GetNextUri(n);

                    string html;
                    try
                    {
                        onIdAction(n);

                        html = await func(uri, cancellationToken).ConfigureAwait(false);

                    }
                    finally
                    {
                        endIdAction(n);
                    }
                    


                    PixivData data = CreatePixivData.Create(n, html);

                    await channelsData.WriteAsync(data).ConfigureAwait(false);

                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch (MHttpClientException e)
                {
                    if (e.InnerException is MHttpResponseException)
                    {

                    }
                    else
                    {
                        Log.Write("client", e);
                    }
                }
            }
        }

        static async Task SaveTask(ChannelReader<PixivData> channelsData, int maxCount, Action<int> setSaveCount)
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
            
            int saveCount = 0;

            Func<Task> addAll = () =>
            {
                var v = list;

                list = new List<PixivData>(maxCount);

                saveCount += v.Count;

                setSaveCount(saveCount);

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
                    var itemTask = channelsData.ReadAsync().AsTask();

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
            catch (ChannelClosedException)
            {

            }

            await addAll().ConfigureAwait(false);
        }

        static Func<Task<string>, TaskReTryFlag> CreateMaxExCountFunc(int maxExCount)
        {
            int count = 0;

            return (task) =>
            {

                if (task.IsCompletedSuccessfully)
                {
                    count = 0;

                }
                else
                {
                    if(task.Exception
                    .InnerException<MHttpClientException>()
                    .InnerException<MHttpResponseException>()
                    .IsNotNull())
                    {
                        if ((count++) >= maxExCount)
                        {
                            throw new ChannelClosedException();
                        }
                    }
                    

                }

                return TaskReTryFlag.None;
            };
        }

        static void AddMaxExCountFunc(TaskReTryBuild<string> build, int? maxExCount)
        {

            if (maxExCount is null)
            {
                
            }
            else
            {
                build.Add(CreateMaxExCountFunc(maxExCount.Value));
            }
        }


        static Func<int, int, Task<List<int>>> CreateGetIdFunc(Crawling.Mode mode)
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


        

        static Func<Task<string>, TaskReTryFlag> CreateCountFunc(CountPack countPack)
        {
            return (task) =>
            {
                if (task.Exception.IsNotNull() &&
                    task.Exception.InnerException is MHttpClientException e)
                {
                    if (e.InnerException is MHttpResponseException)
                    {
                        countPack.Res404++;
                    }

                    if(e.InnerException is OperationCanceledException)
                    {
                        countPack.TimeOut++;
                    }

                }


                return TaskReTryFlag.None;
            };
        }

        static Action<Task> CreateCencelAction(ChannelWriter<int> ids, ChannelWriter<PixivData> datas, CancellationTokenSource source)
        {
            return (t) =>
            {

                ids.TryComplete();

                datas.TryComplete();

                source.Cancel();
            };

        }

        static Func<Uri, CancellationToken, Task<string>> CreateFunc(int? maxExCount, int runCount, TimeSpan responseTimeOut, CountPack countPack)
        {
            
            var build = TaskReTryBuild<string>.Create();

            CreatePixivMHttpClient.AddReTryFunc(build);

            AddMaxExCountFunc(build, maxExCount);

            build.Add(CreateCountFunc(countPack));

            var buildFunc = build.CreateRetryFunc(3, responseTimeOut);

            var func = CreatePixivMHttpClient.CreateProxy(runCount);


            return (uri, tokan) =>
            {
                return buildFunc((tokan) => func(uri, tokan), tokan);
            };
        }

        static Task CreateTask(Func<Uri, CancellationToken, Task<string>> func, CancellationToken cancellationToken, ChannelReader<int> ids, ChannelWriter<PixivData> datas, int runCount, CountPack countPack)
        {
            var ts = new List<Task>();

            Action<int> onIdAction = (n) =>
            {
                countPack.Id = n;

                countPack.Load++;

                countPack.AddHtmlCount();
            };

            Action<int> endIdAction = (n) =>
            {
                countPack.SubHtmlCount();
            };

            foreach (var item in Enumerable.Range(0, runCount))
            {
                ts.Add(Task.Run(() => LoadHtmlLoopTask(func, cancellationToken, ids, datas, onIdAction, endIdAction)));
            }

            return Task.WhenAll(ts.ToArray());
        }

        public static Crawling Start(Crawling.Mode mode, int? maxExCount, int startId, int? endId, int runCount, TimeSpan responseTimeOut)
        {
           

            const int ID_PRE_LOAD_COUNT = 10000;

            startId -= ID_PRE_LOAD_COUNT * 4;

            var countPack = new CountPack();

            var ids = Channel.CreateBounded<int>(ID_PRE_LOAD_COUNT);

            var datas = Channel.CreateBounded<PixivData>(ID_PRE_LOAD_COUNT);

            var source = new CancellationTokenSource();

            var completeFunc = CreateCencelAction(ids, datas, source);

            Task.Run(() => PreLoadIdTask(ids, InitId(startId), ID_PRE_LOAD_COUNT, CreateGetIdFunc(mode), CreateEndFunc(endId)))
                .ContinueWith(completeFunc);

            Task.Run(() => SaveTask(datas, ID_PRE_LOAD_COUNT, (n) => countPack.Save = n))
                .ContinueWith(completeFunc);

            var func = CreateFunc(maxExCount, runCount, responseTimeOut, countPack);

            var allTask = CreateTask(func, source.Token, ids, datas, runCount, countPack).ContinueWith(completeFunc);

            var craw = new Crawling();

            craw.Task = allTask;

            craw.Count = countPack;

            craw.CompleteAdding = () => completeFunc(Task.CompletedTask);

            return craw;
        }

        sealed class CountPack
        {
            
            int m_htmlCount = 0;

            public void AddHtmlCount()
            {
                Interlocked.Increment(ref m_htmlCount);
            }

            public void SubHtmlCount()
            {
                Interlocked.Decrement(ref m_htmlCount);
            }

            public int HtmlCount => m_htmlCount;


            public int Id { get; set; }

            public int Save { get; set; }

            public int Res404 { get; set; }

            public int TimeOut { get; set; }

            public int Load { get; set; }
        }


        private CountPack Count { get; set; }

        public Task Task { get; private set; }

        public int Id => Count.Id;

        public Action CompleteAdding { get; private set; }

        public string Message => $"ID:{Count.Id} L:{Count.Load} S:{Count.Save} R:{Count.Res404} T:{Count.TimeOut} N:{Count.HtmlCount} C:{Task.IsCompleted}";

        private Crawling()
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

        readonly Func<Uri, CancellationToken, Task<byte[]>> m_client;

        readonly string m_basePath;

        public int Count => m_count.Count;

        public LoadBigImg(string basePath, int bigImgResponseSize, TimeSpan responseTimeOut)
        {
            
            Directory.CreateDirectory(basePath);

            m_basePath = basePath;


            var build = TaskReTryBuild<byte[]>.Create();

            CreatePixivMHttpClient.AddReTryFunc(build);

            var buildFunc = build.CreateRetryFunc(6, responseTimeOut);

            var func = CreatePixivMHttpClient.Create(6, bigImgResponseSize);

            m_client = (uri, tokan) => buildFunc((tokan) => func(uri, tokan), tokan);
        }

        async Task SaveImage(byte[] buffer)
        {
            string name = Path.Combine(m_basePath, Path.GetRandomFileName() + ".png");

            using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 1, true))
            {

                await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
        }

        public async Task<byte[]> Load(string path, bool originalImage = false)
        {


            Uri uri = originalImage ? CreatePixivData.GetOriginalUri(path) : CreatePixivData.GetRegularUri(path);

            return await m_client(uri, CancellationToken.None).ConfigureAwait(false);

        }



        async Task LoadCount(string path)
        {
            try
            {
                m_count.AddCount();

                var buffer = await Load(path, true).ConfigureAwait(false);


                await SaveImage(buffer).ConfigureAwait(false);

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
        static async ValueTask<byte[]> GetAsync(int itemId, Func<Task<byte[]>> func)
        {
            var bufferData = await ImgDataBase.Small.Get(itemId).ConfigureAwait(false);

            if (bufferData is null ||
                bufferData.Img is null)
            {
                
                var buffer = await func().ConfigureAwait(false);

                await ImgDataBase.Small.Add(new ImgData(itemId, buffer)).ConfigureAwait(false);

                return buffer;
            }
            else
            {
                return bufferData.Img;
            }
        }

        static async Task CreateLoadImg(Func<Uri, CancellationToken, Task<byte[]>> func, CancellationToken cancellationToken, ChannelReader<PixivData> source, ChannelWriter<Data> destion)
        {
            while (true)
            {
                try
                {
                    var data = await source.ReadAsync().ConfigureAwait(false);

                    byte[] buffer = await GetAsync(data.ItemId, () =>
                    {
                        Uri uri = CreatePixivData.GetSmallUri(data.Path);

                        return func(uri, cancellationToken);
                    }).ConfigureAwait(false);

                    var item = new Data(buffer, data.Path, data.ItemId, data.Tags);

                    await destion.WriteAsync(item).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch(MHttpClientException)
                {

                }

            }
        }

        static async Task CreateLoadData(ChannelWriter<PixivData> destion, Func<Task<List<PixivData>>> func)
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
                catch (ChannelClosedException)
                {
                    return;
                }

            }
        }

        static Action<Task> CreateCencelAction(ChannelWriter<PixivData> pixivDatas, ChannelWriter<Data> datas, CancellationTokenSource source)
        {
            return (t) =>
            {

                pixivDatas.TryComplete();

                datas.TryComplete();

                source.Cancel();
            };
        }

        static Func<Uri, CancellationToken, Task<byte[]>> CreateFunc(int imgLoadCount, int responseSize, TimeSpan responseTimeOut)
        {


            var func = CreatePixivMHttpClient.Create(imgLoadCount, responseSize);

            var build = TaskReTryBuild<byte[]>.Create();

            CreatePixivMHttpClient.AddReTryFunc(build);

            var buildFunc = build.CreateRetryFunc(3, responseTimeOut);

            return (uri, tokan) => buildFunc((tokan) => func(uri, tokan), tokan);
        }

        static Task AddAllTask(int imgLoadCount, Func<Task> func)
        {

            var list = new List<Task>();

            foreach (var item in Enumerable.Range(0, imgLoadCount))
            {
                list.Add(Task.Run(() => func()));
            }


            var t = Task.WhenAll(list.ToArray());

            Log.Write("reload", t);

            return t;

        }

        public static Preload Create(Func<Task<List<PixivData>>> dataFunc, int dataLoadCount, int imgLoadCount, int responseSize, TimeSpan responseTimeOut)
        {

            var datas = Channel.CreateBounded<PixivData>(dataLoadCount);

            var imgs = Channel.CreateBounded<Data>(imgLoadCount);

            var source = new CancellationTokenSource();

            var cencelAction = CreateCencelAction(datas, imgs, source);

            Task.Run(() => CreateLoadData(datas, dataFunc))
                .ContinueWith(cencelAction);

            var func = CreateFunc(imgLoadCount, responseSize, responseTimeOut);

            AddAllTask(imgLoadCount, () => CreateLoadImg(func, source.Token, datas, imgs))
                .ContinueWith(cencelAction);

            var preLoad = new Preload();

            preLoad.Cencel = () => cencelAction(Task.CompletedTask);

            preLoad.Read = () => Task.Run(() => imgs.Reader.ReadAsync(source.Token).AsTask());


            return preLoad;
        }


        Action Cencel { get; set; }

        Func<Task<Data>> Read { get; set; }



        public void Complete()
        {

            Cencel();

        }

        public Task While(Func<Data, Task<TimeSpan>> func)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        if (m_slim.Wait(TimeSpan.Zero))
                        {
                            var data = await Read().ConfigureAwait(false);

                            var timeSpan = await func(data).ConfigureAwait(false);

                            await Task.Delay(timeSpan).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {

                }
                catch (ChannelClosedException)
                {

                }

                


            });
        }


        ManualResetEventSlim m_slim = new ManualResetEventSlim();

        public void SetWait()
        {
            m_slim.Reset();
        }

        public void SetNotWait()
        {
            m_slim.Set();
        }

        private Preload()
        {
            SetNotWait();
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
        public static string WebInfo
        {
            get => Preferences.Get(nameof(WebInfo), WebInfoHelper.GetDefWebInfo());

            set => Preferences.Set(nameof(WebInfo), value);
        }

        public static string CrawlingMaxExCount
        {
            get => Preferences.Get(nameof(CrawlingMaxExCount), "1000");

            set => Preferences.Set(nameof(CrawlingMaxExCount), value);
        }

        public static string CrawlingEndId
        {
            get => Preferences.Get(nameof(CrawlingEndId), "86000201");

            set => Preferences.Set(nameof(CrawlingEndId), value);
        }

        public static int TaskCount
        {
            get => Preferences.Get(nameof(TaskCount), 64);

            set => Preferences.Set(nameof(TaskCount), value);
        }

        public static int CrawlingStartId
        {
            get => Preferences.Get(nameof(CrawlingStartId), 66000201);

            set => Preferences.Set(nameof(CrawlingStartId), value);
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

        public static string MinMark
        {
            get => Preferences.Get(nameof(MinMark), "");

            set=> Preferences.Set(nameof(MinMark), value);
        }

        public static string MaxMark
        {
            get => Preferences.Get(nameof(MaxMark), "");

            set => Preferences.Set(nameof(MaxMark), value);
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

        public static int ViewColumn
        {
            get => Preferences.Get(nameof(ViewColumn), 2);

            set => Preferences.Set(nameof(ViewColumn), value);
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

        static int F(string s, int minNumber)
        {
            if (int.TryParse(s, out int n) && n >= minNumber)
            {
                return n;
            }
            else
            {
                throw new FormatException();
            }

        }

        public static int? CreateNullInt32(string s)
        {
            if (int.TryParse(s, out int n))
            {
                return n;
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

        public static bool Create(string viewColumn, string maxExCount, string endId, string taskCount, string minId, string maxId, string nottag, string id, string min, string max, string tag, string offset, string count)
        {
            try
            {
                MinId = minId ?? "";

                MaxId = maxId ?? "";

                CrawlingEndId = endId ?? "";

                CrawlingMaxExCount = maxExCount ?? "";

                TaskCount = F(taskCount, 1);

                CrawlingStartId = F(id, 0);

                MinMark = min ?? "";

                MaxMark = max ?? "";

                ViewColumn = F(viewColumn, 1);

                Offset = F(offset, 0);

                Count = F(count, 1);


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

            int? min = CreateNullInt32(MinMark);

            int? max = CreateNullInt32(MaxMark);

            int offset = Offset;

            int count = Count;

            int? minId = CreateNullInt32(InputData.MinId);

            int? maxId = CreateNullInt32(InputData.MaxId);

            return () =>
            {
                int n = offset;

                var task = DataBase.Select(minId, maxId, min, max, tag, nottag, offset, count);


                offset += count;

                Offset = n;

                return task;

            };
        }
    }

    public partial class MainPage : ContentPage
    {
        const int SMALL_IMG_RESPONSE_SIZE = 1024 * 1024 * 5;

        const int SMALL_IMG_TIMEOUT = 30;


        const int SMALL_IMG_PERLOAD_COUNT = 12;




        const int BIG_IMG_RESPONSE_SIZE = 1024 * 1024 * 20;

        const int BIG_IMG_TIMEOUT = 240;





        const int CRAWLING_COUNT = 64;

        const int CRAWLING_MAX_EX_COUNT = 1000;

        const int CRAWLING_TIMEOUT = 10;


        const int PIXIVDATA_PRELOAD_COUNT = 200;



        const int IMG_VIEW_COUNT = 400;

        const int IMG_TIMESPAN = 1;

        const int IMG_FLUSH_TIMESPAN = 6;

        const int MESSAGE_FLUSH_TIMESPAN = 5;




        const string ROOT_PATH = "/storage/emulated/0/pixiv/";

        const string BASE_PATH = ROOT_PATH + "database/";
      
        const string IMG_PATH = ROOT_PATH + "img/";

        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();

        readonly LoadBigImg m_download = new LoadBigImg(IMG_PATH, BIG_IMG_RESPONSE_SIZE, new TimeSpan(0, 0, BIG_IMG_TIMEOUT));

        Crawling m_crawling;

        Action m_action;

        Task m_reloadTask;

        Preload m_reload;

        public MainPage(string basePath)
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

                return;
            }

            
            DeviceDisplay.KeepScreenOn = true;


            Directory.CreateDirectory(ROOT_PATH);

            Directory.CreateDirectory(IMG_PATH);



            DataBase.Init(BASE_PATH);
            
            ImgDataBase.Init(BASE_PATH);

            InitInputView();

            Log.Write("viewtext", InitViewText());
           

            InitCollView();

            Pixiv.App.Current.ModalPushed += (obj, e) => m_reload?.SetWait();

            Pixiv.App.Current.ModalPopped += (obj, e) => m_reload?.SetNotWait();
        } 

        void InitCrawlingMoedValue()
        {
            var vs = Enum.GetNames(typeof(Crawling.Mode));

            m_crawling_moed_value.ItemsSource = vs;

            m_crawling_moed_value.SelectedIndex = 0;
        }

        void InitInputView()
        {
            InitCrawlingMoedValue();

            m_max_ex_count_value.Text = InputData.CrawlingMaxExCount;

            m_endId_value.Text = InputData.CrawlingEndId.ToString();

            m_task_count_value.Text = InputData.TaskCount.ToString();

            m_tag_value_histry.ItemsSource = InputData.GetInfo();

            m_max_id_value.Text = InputData.MaxId;

            m_min_id_value.Text = InputData.MinId;

            m_nottag_value.Text = InputData.NotTag;

            m_startId_value.Text = InputData.CrawlingStartId.ToString();

            m_tag_value.Text = InputData.Tag;

            m_min_value.Text = InputData.MinMark.ToString();
           
            m_max_value.Text = InputData.MaxMark.ToString();
            
            m_offset_value.Text = InputData.Offset.ToString();

            m_view_column_value.Text = InputData.ViewColumn.ToString();
           
        }

        bool CreateInput()
        {
            return InputData.Create(
                m_view_column_value.Text,
                m_max_ex_count_value.Text,
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

        void SetCollViewColumn(int viewColumn)
        {
            var v = new GridItemsLayout(viewColumn, ItemsLayoutOrientation.Vertical);

            v.SnapPointsType = SnapPointsType.Mandatory;

            v.SnapPointsAlignment = SnapPointsAlignment.End;

            m_collView.ItemsLayout = v;
        }

        void OnStart(object sender, EventArgs e)
        {
            
            if (CreateInput() == false)
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }
            else
            {
                SetCollViewColumn(InputData.ViewColumn);

                m_cons.IsVisible = false;

                m_reload = Preload.Create(InputData.CreateSelectFunc(), PIXIVDATA_PRELOAD_COUNT, SMALL_IMG_PERLOAD_COUNT, SMALL_IMG_RESPONSE_SIZE, new TimeSpan(0, 0, SMALL_IMG_TIMEOUT));

                m_reloadTask = m_reload.While((data) => MainThread.InvokeOnMainThreadAsync(() => SetImage(data)));

                Log.Write("reload", m_reloadTask);
            }
        }

        TimeSpan SetImage(Data date)
        {

            if (m_imageSources.Count == IMG_VIEW_COUNT)
            {
                m_imageSources.Add(date);

                return new TimeSpan(0, 0, IMG_FLUSH_TIMESPAN);

                
            }
            else if (m_imageSources.Count > IMG_VIEW_COUNT)
            {
                m_imageSources.Clear();

                m_imageSources.Add(date);

                return new TimeSpan(0, 0, IMG_TIMESPAN);

            }
            else
            {
                m_imageSources.Add(date);

                return new TimeSpan(0, 0, IMG_TIMESPAN);

            }


        }

        

        protected override bool OnBackButtonPressed()
        {
            if (!(m_reload is null) && !(m_reloadTask is null))
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
            else
            {
                if (!(m_crawling is null))
                {
                    m_crawling.CompleteAdding();

                    DisplayAlert("消息", "爬取取消中", "确定");
                }
            }

            return true;
        }

        void ViewImagePage(Data data)
        {

            var task = Task.Run(() => m_download.Load(data.Path));

            Action action = () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {

                    Clipboard.SetTextAsync(CreatePixivData.GetNextUri(data.Id).AbsoluteUri + "    " + data.Tags);

                    m_download.Add(data.Path);
                });
            };

            Navigation.PushModalAsync(new ViewImagePage(data.Buffer, task, action));
        }


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collView.SelectedItem != null)
            {
                Data data = (Data)m_collView.SelectedItem;

                ViewImagePage(data);

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
                    m_reload?.SetWait();
                    
                }
                else if (n > 0 && e.LastVisibleItemIndex + 1 == m_imageSources.Count)
                {
                    m_reload?.SetNotWait();
                    
                }
            }
        }

        static Crawling.Mode GetCrawlingMoedValue(string s)
        {
            return (Crawling.Mode)Enum.Parse(typeof(Crawling.Mode), s);

        }

        void CrawlingOver(Task task)
        {
            MainThread.BeginInvokeOnMainThread(() => m_start_cons.IsVisible = true);
        }

        

        void OnStartFromInputId(object sender, EventArgs e)
        {
            if (CreateInput())
            {
                m_start_cons.IsVisible = false;

                MainThread.InvokeOnMainThreadAsync(() =>
                {
                    int id = InputData.CrawlingStartId;

                    int? endId = InputData.CreateNullInt32(InputData.CrawlingEndId);

                    int? maxExCount = InputData.CreateNullInt32(InputData.CrawlingMaxExCount);

                    int taskCount = InputData.TaskCount;

                    var moed = GetCrawlingMoedValue(m_crawling_moed_value.SelectedItem.ToString());

                    m_crawling = Crawling.Start(moed, maxExCount, id, endId, taskCount, new TimeSpan(0, 0, CRAWLING_TIMEOUT));

                    m_crawling.Task.ContinueWith(CrawlingOver);

                    m_action = () => InputData.CrawlingStartId = m_crawling.Id;
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

        void OnSetLastId(object sender, EventArgs e)
        {
            Task tt = DataBase.GetMaxItemId().ContinueWith((t) =>
            {
                int n = t.Result;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    m_startId_value.Text = n.ToString();
                    m_min_id_value.Text = n.ToString();
                });
            });

            Log.Write("setlastid", tt);
        }

        void OnSetWebInfo(object sender, EventArgs e)
        {
            Navigation.PushModalAsync(new InputWebInfoPage());
        }

        void OnDataBaseManagement(object sender, EventArgs e)
        {
            Navigation.PushModalAsync(new DataBaseManagementPage());
        }
    }
}