using System.Collections;
using System.Collections.Immutable;

namespace Servicefy.Package.Pipeline;

/// <summary>
/// An immutable array with value equality, for incremental pipeline models.
/// <see cref="ImmutableArray{T}"/> compares by reference, so two runs that collect the very same
/// rules would never compare equal; this wrapper compares element by element instead.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public ImmutableArray<T> AsImmutableArray() => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => AsImmutableArray().Length;

    public T this[int index] => AsImmutableArray()[index];

    public bool Equals(EquatableArray<T> other) => AsImmutableArray().SequenceEqual(other.AsImmutableArray());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsImmutableArray())
            hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> items) where T : IEquatable<T> => new(items);
}
