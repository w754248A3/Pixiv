using LeiKaiFeng.Http;
using SQLite;
using System;
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

        public static Uri GetOriginalUri(PixivData data)
        {
            return new Uri(ORIGINAL_BASE_PATH + data.Path);
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

        public static string GetLocalPath(PixivData data)
        {
            string[] ss = data.Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            return Path.Combine(ss[0], ss[1], ss[2], ss[ss.Length - 1]);
        }
    }

    public static class CreatePixivMHttpClient
    {
        static async Task<KeyValuePair<Socket, Stream>> Create(Uri uri)
        {
            const string HOST = "www.pixivision.net";

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            await socket.ConnectAsync(HOST, 443).ConfigureAwait(false);

            SslStream sslStream = new SslStream(new NetworkStream(socket, true), false);

            await sslStream.AuthenticateAsClientAsync(HOST).ConfigureAwait(false);


            return new KeyValuePair<Socket, Stream>(socket, sslStream);

        }


        public static MHttpClient Create(int maxStreamPoolCount)
        {
            return new MHttpClient(new MHttpClientHandler
            {
                ConnectCallback = Create,

                MaxStreamPoolCount = maxStreamPoolCount
            });
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

    public static class DataBase
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

        static string DatabasePath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(basePath, DatabaseFilename);

        }


        public static void Init()
        {
            s_slim = new SemaphoreSlim(1, 1);

            s_connection = new SQLiteConnection(DatabasePath(), Flags);

            s_connection.CreateTable<PixivData>();
        }

        static int GetMaxItemId_()
        {
            var datas = s_connection.DeferredQuery<PixivData>($"SELECT {nameof(PixivData.ItemId)} FROM {nameof(PixivData)} ORDER BY {nameof(PixivData.ItemId)} DESC");

            int n = datas.Select((d) => d.ItemId).FirstOrDefault();

            return n == 0 ? START_VALUE : n;
        }

        static object Add_(IEnumerable<PixivData> datas)
        {
            foreach (var item in datas)
            {
                s_connection.InsertOrReplace(item);
            }

            return null;
        }


        static List<PixivData> Select_(int minMark, int maxMark, int offset, int count)
        {
            return s_connection.Query<PixivData>($"SELECT * FROM {nameof(PixivData)} WHERE {nameof(PixivData.Mark)} >= {minMark} AND {nameof(PixivData.Mark)} <= {maxMark} ORDER BY {nameof(PixivData.Mark)} DESC LIMIT {count} OFFSET {offset}");
        }

        static List<PixivData> Select_(int minMark, int maxMark, string tag, int offset, int count)
        {
            return s_connection.Query<PixivData>($"SELECT * FROM {nameof(PixivData)} WHERE {nameof(PixivData.Mark)} >= {minMark} AND {nameof(PixivData.Mark)} <= {maxMark} AND {nameof(PixivData.Tags)} LIKE '%{tag}%' ORDER BY {nameof(PixivData.Mark)} DESC LIMIT {count} OFFSET {offset}");
        }

        static async Task<T> F<T>(Func<T> func)
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


        public static Task<List<PixivData>> Select(int minMark, int maxMark, int offset, int count)
        {
            return F(() => Select_(minMark, maxMark, offset, count));
        }

        public static Task<List<PixivData>> Select(int minMark, int maxMark, string tag, int offset, int count)
        {
            return F(() => Select_(minMark, maxMark, tag, offset, count));
        }


        public static Task<int> GetMaxItemId()
        {
            return F(GetMaxItemId_);
        }


        public static Task Add(IEnumerable<PixivData> datas)
        {
            return F(() => Add_(datas));
        }
    }


    static class Crawling
    {
        static async Task Start(MHttpClient httpClient, Func<int> func)
        {
            var list = new List<PixivData>();
            while (true)
            {
                int itemId = func();

                Uri uri = CreatePixivData.GetNextUri(itemId);

                try
                {

                    string html = await httpClient.GetStringAsync(uri).ConfigureAwait(false);

                    PixivData data = CreatePixivData.Create(itemId, html);

                    list.Add(data);


                    if (list.Count >= 10)
                    {
                        await DataBase.Add(list);

                        list = new List<PixivData>();
                    }
                }
                catch (MHttpClientException)
                {

                }
                catch (FormatException)
                {

                }

            }
        }


        static void Start(int count, MHttpClient httpClient, Func<int> func)
        {
            Task.Run(async () =>
            {
                var list = new List<Task>();
                foreach (var item in Enumerable.Range(0, count))
                {
                    list.Add(Start(httpClient, func));
                }


                while (true)
                {
                    Task t = await Task.WhenAny(list.ToArray()).ConfigureAwait(false);

                    list.RemoveAll((v) => object.ReferenceEquals(t, v));

                    list.Add(Start(httpClient, func));
                }

            });
        }

        public static async void Start()
        {
            const int COUNT = 32;

            int n = await DataBase.GetMaxItemId();

            Func<int> func = () => Interlocked.Increment(ref n);

            MHttpClient httpClient = CreatePixivMHttpClient.Create(COUNT);

            Crawling.Start(COUNT, httpClient, func);
        }
    }

    static class EnumImage
    {


        static async Task<KeyValuePair<byte[], string>> CreateItem(Task<byte[]> task, string path)
        {
            byte[] buffer = await task.ConfigureAwait(false);

            return new KeyValuePair<byte[], string>(buffer, path);

        }

        static async Task<KeyValuePair<byte[], string>> CreateBufferAsync(Task task)
        {
            await task.ConfigureAwait(false);

            throw new TaskCanceledException();
        }

        static async Task<byte[]> GetImgAsync(MHttpClient client, PixivData data)
        {
            byte[] buffer = await ImgLocalBuffer.GetBufferImage(data).ConfigureAwait(false);

            if(buffer is null)
            {
                Uri uri = CreatePixivData.GetOriginalUri(data);


                Uri referer = new Uri("https://www.pixiv.net/");


                buffer = await client.GetByteArrayAsync(uri, referer).ConfigureAwait(false);

                Task t = ImgLocalBuffer.WriteImg(data, buffer);


                return buffer;
            }
            else
            {
                return buffer;
            }
        }

        static IEnumerable<Task<KeyValuePair<byte[], string>>> Create(Func<int, int, Task<List<PixivData>>> func)
        {
            MHttpClient client = new MHttpClient();

            Uri referer = new Uri("https://www.pixiv.net/");

            int offset = 0;

            int count = 200;

            while (true)
            {
                var task = func(offset, count);

                while (!task.IsCompleted)
                {
                    yield return CreateBufferAsync(task);
                }

                if(task.Status != TaskStatus.RanToCompletion)
                {
                    yield break;
                }

                var list = task.Result;

                if (list.Count == 0)
                {
                    yield break;
                }

                foreach (var item in list)
                {
                    
                    string path = item.Path;
                    var t = GetImgAsync(client, item);
                    yield return CreateItem(t, path);
                }


                offset += count;
            }


        }


        static IEnumerable<Task<KeyValuePair<byte[], string>>> Pl(IEnumerable<Task<KeyValuePair<byte[], string>>> e, int count)
        {
            var queue = new Queue<Task<KeyValuePair<byte[], string>>>(count);

            foreach (var item in e)
            {
                queue.Enqueue(item);

                if (queue.Count >= count)
                {
                    yield return queue.Dequeue();
                }

            }


            while (queue.Count != 0)
            {
                yield return queue.Dequeue();
            }
        }


        public static IEnumerable<Task<KeyValuePair<byte[], string>>> Create(Func<int, int, Task<List<PixivData>>> func, int count)
        {
            var e = Create(func);


            return Pl(e, count);
        }
    }


    sealed class Data
    {
        public Data(byte[] buffer, string path)
        {
            Path = path;

            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));
        }

        public string Path { get; }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }


    }

    static class ImgLocalBuffer
    {
        static readonly string s_path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImgBuffer");


        public static async Task<byte[]> GetBufferImage(PixivData data)
        {
           
            try
            {
                string path = Path.Combine(s_path, CreatePixivData.GetLocalPath(data));

                using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    byte[] buffer = new byte[fileStream.Length];

                    await fileStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

                    return buffer;
                }
            }
            catch (Exception e)
            {
                return null;
            }
        } 


        public static async Task WriteImg(PixivData data, byte[] buffer)
        {
            try
            {
                
                string path = Path.Combine(s_path, CreatePixivData.GetLocalPath(data));

                using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)) 
                {
                    await fileStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                }     
            }
            catch(Exception e)
            {

            }
        }
    }

    public partial class MainPage : ContentPage
    {
        const int COLLVIEW_COUNT = 16;

        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();


        public MainPage()
        {
            InitializeComponent();

            DeviceDisplay.KeepScreenOn = true;


            DataBase.Init();


            Crawling.Start();

          
            InitViewText();

            InitCollView();

        }

        async void InitViewText()
        {
            while (true)
            {
                m_viewText.Text = (await DataBase.GetMaxItemId()).ToString();

                await Task.Delay(new TimeSpan(0, 0, 5));
            }
        } 

        void InitCollView()
        {
            m_collView.ItemsSource = m_imageSources;

            foreach (var item in Enumerable.Range(0, COLLVIEW_COUNT))
            {
                m_imageSources.Add(new Data(Array.Empty<byte>(), string.Empty));
            }

        }

        bool Chuck()
        {
            if (string.IsNullOrWhiteSpace(m_min_value.Text) ||
                string.IsNullOrWhiteSpace(m_max_value.Text))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        void OnStart(object sender, EventArgs e)
        {
            m_cons.IsVisible = false;

            if (Chuck())
            {
                Start();
            }
            else
            {
                Task t = DisplayAlert("错误", "必须输入参数", "确定");
            }
        }

        Func<int, int, Task<List<PixivData>>> Create()
        {
            int min = int.Parse(m_min_value.Text);

            int max = int.Parse(m_max_value.Text);

            string tag = m_tag_value.Text;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return (offset, count) => DataBase.Select(min, max, offset, count);
            }
            else
            {
                return (offset, count) => DataBase.Select(min, max, tag, offset, count);
            }
        }

        async Task FlushView()
        {
            await Task.Yield();
        }

        Task SetImage(byte[] buffer, string path)
        {
            var date = new Data(buffer, path);

            m_imageSources.RemoveAt(0);

            m_imageSources.Add(date);


            m_collView.ScrollTo(m_imageSources.Count - 1, position: ScrollToPosition.End, animate: false);

            return FlushView();
        }


        async void Start()
        {
            foreach (var item in EnumImage.Create(Create(), 6))
            {
                try
                {
                    var v = await item;

                    await SetImage(v.Key, v.Value);

                    await Task.Delay(new TimeSpan(0, 0, 1));
                }
                catch (Exception e)
                {

                }
            }
        }
    }
}