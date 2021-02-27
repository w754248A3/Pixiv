using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{

    public sealed class ListImageBindData
    {
        public ListImageBindData(byte[] buffer, string path, int id, string tags)
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

    public sealed class ViewListImagePageInfo
    {
        public ViewListImagePageInfo(int viewColumn, LoadBigImg loadBigImg)
        {
            ViewColumn = viewColumn;
            LoadBigImg = loadBigImg;

            TaskSource = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public int ViewColumn { get; }

        public LoadBigImg LoadBigImg { get; }

        public Task Task => TaskSource.Task;

        public TaskCompletionSource<object> TaskSource { get; }
    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewListImagePage : ContentPage
    {
        readonly ObservableCollection<ListImageBindData> m_imageSources = new ObservableCollection<ListImageBindData>();

        ViewListImagePageInfo m_info;

        Preload m_preload;

        public ViewListImagePage(ViewListImagePageInfo info)
        {
            InitializeComponent();

            m_info = info;

            InitCollView();

            SetCollViewColumn(info.ViewColumn);

            Start();
        }

        //void InitEvent()
        //{
        //    EventHandler<ModalPushedEventArgs> push = (obj, e) => m_preload.SetWaitModePage();

        //    Pixiv.App.Current.ModalPushed += push;

        //    EventHandler<ModalPoppedEventArgs> pop = (obj, e) => m_preload.SetNotWaitModePage();

        //    Pixiv.App.Current.ModalPopped += pop;

        //}

        void Start()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {


                m_preload = Preload.Create(InputData.CreateSelectFunc(), ConstInfo.PIXIVDATA_PRELOAD_COUNT, ConstInfo.SMALL_IMG_PERLOAD_COUNT, ConstInfo.SMALL_IMG_RESPONSE_SIZE, new TimeSpan(0, 0, ConstInfo.SMALL_IMG_TIMEOUT));

                var t = m_preload.While((data) => MainThread.InvokeOnMainThreadAsync(() => SetImage(data)));

                Log.Write("reload", t);
            });
        }

       
        void InitCollView()
        {
            m_collectionView.ItemsSource = m_imageSources;
        }

        void SetCollViewColumn(int viewColumn)
        {
            var v = new GridItemsLayout(viewColumn, ItemsLayoutOrientation.Vertical)
            {
                SnapPointsType = SnapPointsType.Mandatory,

                SnapPointsAlignment = SnapPointsAlignment.End
            };

            m_collectionView.ItemsLayout = v;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collectionView.SelectedItem != null)
            {
                ListImageBindData data = (ListImageBindData)m_collectionView.SelectedItem;

                Clipboard.SetTextAsync(CreatePixivData.GetNextUri(data.Id).AbsoluteUri + "    " + data.Tags);


                ViewImagePage(data);

                m_collectionView.SelectedItem = null;
            }
        }

        void OnScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            long n = (long)e.VerticalDelta;

            if (n != 0)
            {
                if (n < 0)
                {
                    m_preload.SetWait();

                }
                else if (n > 0 && e.LastVisibleItemIndex + 1 == m_imageSources.Count)
                {
                    m_preload.SetNotWait();

                }
            }
        }


        protected override bool OnBackButtonPressed()
        {

            var task = DisplayAlert("提示", "确定返回？", "是的", "按错了");

            task.ContinueWith((t) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (t.Result)
                    {
                        m_preload.Complete();

                        m_info.TaskSource.TrySetResult(default);

                        Navigation.PopModalAsync();
                    }
                });
            });



            return true;
        }

        void ViewImagePage(ListImageBindData data)
        {

            var result = m_info.LoadBigImg.LoadAsync(data);



            var info = new ViewImagePageInfo(data.Buffer, result.Task, result.Save);

            info.Task.ContinueWith((t) => m_preload.SetNotWait());

            m_preload.SetWait();



            Navigation.PushModalAsync(new ViewImagePage(info));
        }



        TimeSpan SetImage(ListImageBindData date)
        {

            if (m_imageSources.Count == ConstInfo.IMG_VIEW_COUNT)
            {
                m_imageSources.Add(date);

                return new TimeSpan(0, 0, ConstInfo.IMG_FLUSH_TIMESPAN);


            }
            else if (m_imageSources.Count > ConstInfo.IMG_VIEW_COUNT)
            {
                m_imageSources.Clear();

                m_imageSources.Add(date);

                return new TimeSpan(0, 0, ConstInfo.IMG_TIMESPAN);

            }
            else
            {
                m_imageSources.Add(date);

                return new TimeSpan(0, 0, ConstInfo.IMG_TIMESPAN);

            }


        }
    }
}