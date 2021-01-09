using LeiKaiFeng.Http;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace Pixiv
{
    static class Log
    {
        static readonly object s_lock= new object();

        public static void Write(string name, Exception e)
        {
            lock (s_lock)
            {
                string s = System.Environment.NewLine;

                File.AppendAllText($"/storage/emulated/0/pixiv.{name}.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{e}", System.Text.Encoding.UTF8);
            }
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

        static async Task<T> GetAsync<T>(Func<Task<T>> func, int reloadCount)
        {

            while (true)
            {
                try
                {
                    return await func().ConfigureAwait(false);
                }
                catch (MHttpClientException e)
                when (e.InnerException is ObjectDisposedException ||
                        e.InnerException is IOException ||
                        e.InnerException is SocketException)
                {
                    await Task.Delay(new TimeSpan(0, 0, 2)).ConfigureAwait(false);
                }
            }

        }

        public static Func<Uri, Task<string>> CreateProxy(int maxStreamPoolCount, int reloadCount)
        {
            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = CreateConnectAsync,

                AuthenticateCallback = CreateAuthenticateAsync,

                MaxStreamPoolCount = checked(maxStreamPoolCount * 2)

  
            });

            client.ConnectTimeOut = new TimeSpan(0, 0, 6);


            client.ResponseTimeOut = new TimeSpan(0, 0, 9);


            return (uri) =>
            {
                return GetAsync(() => client.GetStringAsync(uri), reloadCount);
            };
        }

        public static Func<Uri, Uri, Task<byte[]>> Create(int maxStreamPoolCount, int maxResponseSize, int reloadCount)
        {
            MHttpClient client = new MHttpClient(new MHttpClientHandler
            {
                MaxStreamPoolCount = checked(maxStreamPoolCount * 2),
                MaxResponseSize = maxResponseSize
                
            });

            return (uri, referer) =>
            {
                return GetAsync(() => client.GetByteArrayAsync(uri, referer), reloadCount);
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
        const int START_VALUE = 66000201;

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

        static List<PixivData> s_list = new List<PixivData>();

        static int s_maxCount;

        static string DatabasePath()
        {
            
            return Path.Combine(s_basePath, DatabaseFilename);

        }

        static void Create()
        {
            s_connection = new SQLiteConnection(DatabasePath(), Flags);

            s_connection.CreateTable<PixivData>();
        }

        public static void Init(string basePath, int maxCount)
        {
            s_basePath = basePath;

            s_maxCount = maxCount;

            Directory.CreateDirectory(s_basePath);

            s_slim = new SemaphoreSlim(1, 1);

            Create();
        }

        static int GetMaxItemId_()
        {
            //var datas = s_connection.Query<PixivData>($"SELECT {nameof(PixivData.ItemId)} FROM {nameof(PixivData)} ORDER BY {nameof(PixivData.ItemId)} DESC LIMIT 1");

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

        static object Add_(PixivData data)
        {
            s_list.Add(data);

            if (s_list.Count >= s_maxCount)
            {
                var vs = s_list.ToArray();

                s_list.Clear();

                s_connection.RunInTransaction(() =>
                {

                    foreach (var item in vs)
                    {
                        s_connection.InsertOrReplace(item);
                    }

                });


                return null;
            }
            else
            {
                return null; 
            }
            
        }

        static PixivData Find_(int id)
        {
            return s_connection.Find<PixivData>(id);
        }

        static List<PixivData> Select_((int minId, int maxId)? idSpan, int minMark, int maxMark, string tag, string notTag, int offset, int count)
        {
            //string s = $"SELECT * FROM {nameof(PixivData)}";

            //s += $" WHERE {nameof(PixivData.Mark)} >= {minMark} AND {nameof(PixivData.Mark)} <= {maxMark}";

            //if (idSpan.HasValue)
            //{
            //    var v = idSpan.Value;

            //    s += $" AND {nameof(PixivData.ItemId)} >= {v.minId} AND {nameof(PixivData.ItemId)} <= {v.maxId}";
            //}

            //if (string.IsNullOrWhiteSpace(tag) == false)
            //{
            //    s += $" AND {nameof(PixivData.Tags)} LIKE '%{tag}%'";
            //}


            //if (string.IsNullOrWhiteSpace(notTag) == false)
            //{
            //    s += $" AND {nameof(PixivData.Tags)} NOT LIKE '%{notTag}%'";
            //}

            //s += $" ORDER BY {nameof(PixivData.Mark)} DESC";

            //s += $" LIMIT {count} OFFSET {offset}";

            //return s;


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

        public static Task Add(PixivData data)
        {
            return F(() => Add_(data));
        }

        public static Task<PixivData> Find(int id)
        {
            return F(() => Find_(id));
        }
    }


    sealed class Crawling
    {
        sealed class CrawlingCount
        {
            readonly int m_max_ex_count;

            volatile int m_id;

            volatile int m_ex_count;


            public CrawlingCount(int max_ex_count, int id)
            {
                m_max_ex_count = max_ex_count;

                m_id = id;

                m_ex_count = 0;
            }


            public int Id()
            {
                return m_id;
            }

            public int GetNextId()
            {
                return Interlocked.Increment(ref m_id);
            }

            public void Completed()
            {

                m_ex_count = 0;

            }


            public bool IsReturn()
            {
                if (Interlocked.Increment(ref m_ex_count) >= m_max_ex_count)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }


        readonly Func<Uri, Task<string>> m_client;

        readonly CrawlingCount m_count;

        public Task Task { get; private set; }

        public int Id => m_count.Id();

        public string Message => $"Id:{Id} IsCompleted:{Task.IsCompleted}";

        private Crawling(int startId, int runCount, int maxExCount, int reloadCount)
        {

            m_client = CreatePixivMHttpClient.CreateProxy(runCount, reloadCount);

            m_count = new CrawlingCount(maxExCount, startId);

        }

        int GetNextId()
        {
            return m_count.GetNextId();
        }

        async Task WhileAsync()
        {
            while (true)
            {

                try
                {
                    int n = GetNextId();

                    Uri uri = CreatePixivData.GetNextUri(n);

                    string html = await m_client(uri).ConfigureAwait(false);

                    m_count.Completed();

                    PixivData data = CreatePixivData.Create(n, html);

                    await DataBase.Add(data).ConfigureAwait(false);
                }
                catch (MHttpClientException e)
                when (e.InnerException.GetType() == typeof(MHttpResponseException))
                {
                    if (m_count.IsReturn())
                    {
                        return;
                    }
                }
                catch (MHttpClientException)
                {
                    
                }
            }
        }

        public static Crawling Start(int startId, int runCount, int maxExCount, int reloadCount)
        {
            Crawling crawling = new Crawling(startId, runCount, maxExCount, reloadCount);

            crawling.Task = Task.Run(() =>
            {
                List<Task> tasks = new List<Task>();

                foreach (var item in Enumerable.Range(0, runCount))
                {
                    tasks.Add(crawling.WhileAsync());
                }


                return Task.WhenAll(tasks.ToArray());
            });

            return crawling;
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

        public LoadBigImg(string basePath)
        {
            
            Directory.CreateDirectory(basePath);

            m_basePath = basePath;


            m_client = CreatePixivMHttpClient.Create(6, 1024 * 1024 * 20, 6);
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
            catch (MHttpClientException)
            {

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

    static class EnumImage
    {
        


        static Task<byte[]> GetImageFromWebAsync(Func<Uri, Uri, Task<byte[]>> func, PixivData data)
        {
            Uri uri = CreatePixivData.GetSmallUri(data);


            Uri referer = new Uri("https://www.pixiv.net/");


            return func(uri, referer);
        }

        static async Task CreateLoadImg(Func<Uri, Uri, Task<byte[]>> func, MyChannels<Task<PixivData>> pixivDatas, MyChannels<Task<Data>> imgs)
        {
            while (true)
            {
                try
                {

                    var data = await (await pixivDatas.ReadAsync().ConfigureAwait(false)).ConfigureAwait(false);

                    byte[] buffer = await GetImageFromWebAsync(func, data).ConfigureAwait(false);


                    await imgs.WriteAsync(Task.FromResult(new Data(buffer, data.Path, data.ItemId))).ConfigureAwait(false);

                }
                catch (Exception e)
                {

                    await imgs.WriteAsync(Task.FromException<Data>(e)).ConfigureAwait(false);
                }

            }
        }

        static async Task CreateLoadData(MyChannels<Task<PixivData>> coll, Func<Task<List<PixivData>>> func)
        {
            try
            {

                while (true)
                {

                    var list = await func().ConfigureAwait(false);

                    if (list.Count == 0)
                    {
                        return;
                    }

                    foreach (var item in list)
                    {
                        await coll.WriteAsync(Task.FromResult(item)).ConfigureAwait(false);
                    }

                }
            }
            catch(Exception e)
            {
                await coll.WriteAsync(Task.FromException<PixivData>(e)).ConfigureAwait(false);
            }

        }


        public static MyChannels<Task<Data>> Create(Func<Task<List<PixivData>>> func, int dataLoadCount, int imgLoadCount)
        {

            var datas = new MyChannels<Task<PixivData>>(dataLoadCount);

            var imgs = new MyChannels<Task<Data>>(imgLoadCount);

            var client = CreatePixivMHttpClient.Create(imgLoadCount, 1024 * 1024 * 5, 6);


            Task.Run(() => CreateLoadData(datas, func));

            foreach (var item in Enumerable.Range(0, imgLoadCount)) 
            {
                Task.Run(() => CreateLoadImg(client, datas, imgs));
            }

            return imgs;
        }
    }


    public sealed class Data
    {
        public Data(byte[] buffer, string path, int id)
        {
            Id = id;

            Path = path;

            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));
        }

        public int Id { get; }

        public string Path { get; }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }


    }

    static class InputData
    {
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

        public static bool Create(string minId, string maxId, string nottag, string id, string min, string max, string tag, string offset, string count)
        {
            try
            {

                if (CreateIdSpan(minId, maxId).HasValue == false)
                {
                    return false;
                }

                MinId = minId ?? "";

                MaxId = maxId ?? "";


                Id = F(id);

                Min = F(min);

                Max = F(max);

                Offset = F(offset);

                Count = F(count);


                NotTag = nottag ?? "";

                Tag = tag ?? "";

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
        const int COLLVIEW_COUNT = 400;

        const int SELCT_COUNT = 200;

        const int CRAWLING_COUNT = 64;

        const int LOADIMG_COUNT = 6;

        const int MAX_EX_COUNT = 1000;

        const int RE_LOADcOUNT = 3;


        const int DATEBASE_BUFFER_COUNT = 100;


        const string ROOT_PATH = "/storage/emulated/0/pixiv/";

        const string BASE_PATH = ROOT_PATH + "database/";
      
        const string IMG_PATH = ROOT_PATH + "img/";

        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();

        readonly LoadBigImg m_download = new LoadBigImg(IMG_PATH);

        readonly Awa m_awa = new Awa();

        Crawling m_crawling;

        Action m_action;

        public MainPage()
        {
            InitializeComponent();


            Task t = Init();
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



            DataBase.Init(BASE_PATH, DATEBASE_BUFFER_COUNT);

            InitInputView();

            Task tt = InitViewText();

            InitCollView();
        } 

        void InitInputView()
        {
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
            return InputData.Create(m_min_id_value.Text, m_max_id_value.Text, m_nottag_value.Text, m_startId_value.Text, m_min_value.Text, m_max_value.Text, m_tag_value.Text, m_offset_value.Text, SELCT_COUNT.ToString());
        }


        async Task InitViewText()
        {
            while (true)
            {
                if(m_crawling is null)
                {
                    m_viewText.Text = $"D:{m_download.Count}";
                }
                else
                {
                    m_viewText.Text = $"{m_crawling.Message} D:{m_download.Count}";
                }

                if(m_action != null)
                {
                    m_action();
                }

                await Task.Delay(new TimeSpan(0, 0, 5));
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

                Task t = Start();
            }
        }

        async Task SetImage(Data date)
        {

            if (m_imageSources.Count >= COLLVIEW_COUNT)
            {
                m_imageSources.Clear();

                await Task.Yield();
            }

            m_imageSources.Add(date);

            await Task.Yield();

            m_collView.ScrollTo(m_imageSources.Count - 1, position: ScrollToPosition.End, animate: false);

            await Task.Yield();
        }

        

        async Task Start()
        {

            var imgs = EnumImage.Create(InputData.CreateSelectFunc(), 64, LOADIMG_COUNT);

            while (true)
            {

                try
                {
                    await m_awa.Get();

                    var data = await await imgs.ReadAsync();

                    await SetImage(data);

                    await Task.Delay(1000);
                }
                catch (MHttpClientException) 
                {

                }
                catch (Exception e)
                {
                    Log.Write("pixivEx", e);
                }

            } 
        }

       


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collView.SelectedItem != null)
            {
                Data data = (Data)m_collView.SelectedItem;

                Clipboard.SetTextAsync(CreatePixivData.GetNextUri(data.Id).AbsoluteUri);

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

                m_crawling = Crawling.Start(id, CRAWLING_COUNT, MAX_EX_COUNT, RE_LOADcOUNT);

            });
        }

        void OnStartFromInputId(object sender, EventArgs e)
        {
            if (CreateInput())
            {
                m_start_cons.IsVisible = false;

                MainThread.InvokeOnMainThreadAsync(() =>
                {
                    int id = InputData.Id;


                    m_crawling = Crawling.Start(id, CRAWLING_COUNT, MAX_EX_COUNT, RE_LOADcOUNT);


                    m_action = () => InputData.Id = m_crawling.Id;
                });
            }
            else
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }  
        }
    }
}