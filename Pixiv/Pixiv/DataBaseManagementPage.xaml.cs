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



        void OnDelete(object sender, EventArgs e)
        {
            
            if (int.TryParse(m_min_mark_value.Text, out int minMark) && minMark >= 0)
            {
                m_min_mark_value.IsEnabled = false;

                DataBase.Delete(minMark)
                    .ContinueWith((t) =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            try
                            {
                                int n = t.Result;

                                DisplayAlert("消息", $"删除成功，删掉{n}项数据", "确定");
                            }
                            catch (AggregateException e)
                            {
                                DisplayAlert("消息", e.InnerException.Message, "确定");
                            }
                            finally
                            {
                                m_min_mark_value.IsEnabled = true;
                            }
                        });     
                    });


            }
            else
            {
                DisplayAlert("消息", "输入错误", "确定");
            }
        }
    }
}