using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfTaskBar.Converters
{
    public class IndexToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ListBoxItem item && item.DataContext != null)
            {
                var listBox = ItemsControl.ItemsControlFromItemContainer(item) as ListBox;
                if (listBox != null)
                {
                    int index = listBox.ItemContainerGenerator.IndexFromContainer(item);
                    int itemCount = listBox.Items.Count;
                    
                    if (index == 0 && index == itemCount - 1)
                    {
                        return new Thickness(0, 0, 0, 0); // 唯一の要素は全マージン0
                    }
                    else if (index == 0)
                    {
                        return new Thickness(0, 0, 0, 2); // 最初の要素は上マージン0
                    }
                    else if (index == itemCount - 1)
                    {
                        return new Thickness(0, 2, 0, 0); // 最後の要素は下マージン0
                    }
                }
            }
            return new Thickness(0, 2, 0, 2); // その他の要素は通常のマージン
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}