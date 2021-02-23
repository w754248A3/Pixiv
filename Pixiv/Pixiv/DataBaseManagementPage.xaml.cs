using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class DataBaseManagementPage : ContentPage
    {
        public DataBaseManagementPage()
        {
            InitializeComponent();
        }



        static void Delete(ContentPage contentPage, string s, Button button, Func<int, Task<int>> func)
        {
            if (int.TryParse(s, out int minMark) && minMark >= 0)
            {
                button.IsEnabled = false;

                func(minMark)
                    .ContinueWith((t) =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                int n = t.Result;

                                contentPage.DisplayAlert("消息", $"删除成功，删掉{n}项数据", "确定");
                            }
                            catch (AggregateException e)
                            {
                                contentPage.DisplayAlert("消息", e.InnerException.Message, "确定");
                            }
                            finally
                            {
                                button.IsEnabled = true;
                            }
                        });     
                    });


            }
            else
            {
                contentPage.DisplayAlert("消息", "输入错误", "确定");
            }
        }

        static void Vacuum(ContentPage contentPage, Button button, Func<Task> func)
        {
           
            button.IsEnabled = false;

            func()
                .ContinueWith((t) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            t.Wait();

                            contentPage.DisplayAlert("消息", $"紧缩成功", "确定");
                        }
                        catch (AggregateException e)
                        {
                            contentPage.DisplayAlert("消息", e.InnerException.Message, "确定");
                        }
                        finally
                        {
                            button.IsEnabled = true;
                        }
                    });
                });
        }

        private void OnDeletePixivDataBase(object sender, EventArgs e)
        {
            Delete(this, m_min_value.Text, (Button)sender, DataBase.Delete);
        }

        private void OnDeleteImageDataBase(object sender, EventArgs e)
        {
            Delete(this, m_min_value.Text, (Button)sender, ImgDataBase.Small.Delete);
        }

        private void OnVacuumPixivDataBase(object sender, EventArgs e)
        {
            Vacuum(this, (Button)sender, DataBase.Vacuum);
        }

        private void OnVacuumImageDataBase(object sender, EventArgs e)
        {
            Vacuum(this, (Button)sender, ImgDataBase.Small.Vacuum);
        }
    }
}