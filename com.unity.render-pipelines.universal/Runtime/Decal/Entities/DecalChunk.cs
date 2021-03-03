using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using UnityEngine.Rendering.Universal;

public abstract class DecalChunk : IDisposable
{
    public int count { get; protected set; }
    public int capacity { get; protected set; }

    public JobHandle currentJobHandle { get; set; }

    public abstract void Push();
    public abstract void RemoveAtSwapBack(int index);
    public abstract void SetCapacity(int capacity);

    public virtual void Dispose() {}

    protected void ResizeNativeArray(ref TransformAccessArray array, DecalProjector[] decalProjectors, int capacity)
    {
        var newArray = new TransformAccessArray(capacity);
        if (array.isCreated)
        {
            for (int i = 0; i < array.length; ++i)
                newArray.Add(decalProjectors[i].transform);
            array.Dispose();
        }
        array = newArray;
    }

    protected void ResizeNativeArray<T>(ref NativeArray<T> array, int capacity) where T : struct
    {
        var newArray = new NativeArray<T>(capacity, Allocator.Persistent);
        if (array.IsCreated)
        {
            NativeArray<T>.Copy(array, newArray, array.Length);
            //newArray.CopyFrom(array);


            array.Dispose();
        }
        array = newArray;
    }

    protected void ResizeArray<T>(ref T[] array, int capacity)
    {
        if (array == null)
        {
            array = new T[capacity];
            return;
        }

        Array.Resize<T>(ref array, capacity);
    }

    protected void RemoveAtSwapBack<T>(ref NativeArray<T> array, int index, int count) where T : struct
    {
        array[index] = array[count - 1];
    }

    protected void RemoveAtSwapBack<T>(ref T[] array, int index, int count)
    {
        array[index] = array[count - 1];
    }
}
