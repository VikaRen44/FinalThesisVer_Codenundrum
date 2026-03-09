using System.Collections.Generic;
using UnityEngine;

public class HeapManager : MonoBehaviour
{
    public List<int> heap = new List<int>();

    public void GenerateRandomHeap(int count)
    {
        // Default now supports up to 3-digit values
        GenerateRandomHeap(count, 1, 1000); // 1 to 999
    }

    public void GenerateRandomHeap(int count, int minInclusive, int maxExclusive)
    {
        heap.Clear();

        if (maxExclusive <= minInclusive)
            maxExclusive = minInclusive + 1;

        for (int i = 0; i < count; i++)
        {
            heap.Add(Random.Range(minInclusive, maxExclusive));
        }
    }

    public void BuildHeap()
    {
        for (int i = heap.Count / 2 - 1; i >= 0; i--)
        {
            Heapify(i, heap.Count);
        }
    }

    void Heapify(int i, int heapSize)
    {
        int largest = i;
        int left = 2 * i + 1;
        int right = 2 * i + 2;

        if (left < heapSize && heap[left] > heap[largest])
            largest = left;

        if (right < heapSize && heap[right] > heap[largest])
            largest = right;

        if (largest != i)
        {
            int temp = heap[i];
            heap[i] = heap[largest];
            heap[largest] = temp;

            Heapify(largest, heapSize);
        }
    }

    public int ExtractMax()
    {
        if (heap.Count == 0)
            return -1;

        int max = heap[0];

        heap[0] = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);

        if (heap.Count > 0)
            Heapify(0, heap.Count);

        return max;
    }
}