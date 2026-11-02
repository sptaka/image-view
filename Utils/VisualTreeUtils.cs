using System.Windows;
using System.Windows.Media;

namespace SampleViewer.Utils
{
    public static class VisualTreeUtils
    {
        // 子孫要素のうち型が一致するものをすべて列挙する
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject? obj)
            where T : DependencyObject
        {
            if (obj == null)
                yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);

                if (child is T t)
                    yield return t;

                foreach (var c in FindVisualChildren<T>(child))
                    yield return c;
            }
        }

        // 子孫要素のうち型が一致する最初のもの（深さ優先）
        public static T? FindVisualChild<T>(DependencyObject? obj)
            where T : DependencyObject
        {
            if (obj == null)
                return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);

                if (child is T t)
                    return t;

                var result = FindVisualChild<T>(child);

                if (result != null)
                    return result;
            }

            return null;
        }

        public static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur == ancestor)
                    return true;
            }
            return false;
        }
    }
}
