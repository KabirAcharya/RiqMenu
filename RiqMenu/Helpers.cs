using System;

namespace RiqMenu {
    public static class Helpers {
        public static T[] GetSubArray<T>(this T[] data, int index, int end) {
            int len = (end - index);
            T[] result = new T[len];
            Array.Copy(data, index, result, 0, len);
            return result;
        }
    }
}