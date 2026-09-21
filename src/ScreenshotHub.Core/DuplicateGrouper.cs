using System.Numerics;

namespace ScreenshotHub.Core;

public static class DuplicateGrouper
{
    public const int SimilarityHammingThreshold = 6;
    private const int MaximumCandidatesPerImage = 1_024;
    private const int MaximumCandidatesPerBand = 256;

    public static IReadOnlyDictionary<string, DuplicateMembership> Build(
        IReadOnlyCollection<ScreenshotAnalysisRecord> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        var records = analyses
            .Where(record => record.DifferenceHash is not null || record.Sha256 is not null)
            .OrderBy(record => record.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = records.ToDictionary(
            record => record.FilePath,
            _ => new DuplicateMembership(null, 0, null, 0),
            StringComparer.OrdinalIgnoreCase);

        var exactGroups = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Sha256))
            .GroupBy(record => record.Sha256!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < exactGroups.Length; index++)
        {
            var group = exactGroups[index].ToArray();
            var groupId = $"E{index + 1:N0}";
            foreach (var record in group)
            {
                var current = result[record.FilePath];
                result[record.FilePath] = current with
                {
                    ExactGroupId = groupId,
                    ExactGroupCount = group.Length
                };
            }
        }

        var comparable = records
            .Where(record => record.DifferenceHash is not null && record.PixelWidth > 0 && record.PixelHeight > 0)
            .ToArray();
        var union = new UnionFind(comparable.Length);
        var buckets = new Dictionary<(int Band, ushort Value), List<int>>();
        for (var index = 0; index < comparable.Length; index++)
        {
            var record = comparable[index];
            var hash = record.DifferenceHash!.Value;
            var candidates = new HashSet<int>();
            // Eight 8-bit bands guarantee that hashes within the six-bit threshold
            // share at least one unchanged band (before the explicit Hamming check).
            for (var band = 0; band < 8; band++)
            {
                var value = (ushort)((hash >> (band * 8)) & 0xFF);
                if (!buckets.TryGetValue((band, value), out var bucket))
                {
                    bucket = [];
                    buckets[(band, value)] = bucket;
                }

                foreach (var candidate in bucket.TakeLast(MaximumCandidatesPerBand))
                {
                    if (candidates.Count >= MaximumCandidatesPerImage)
                    {
                        break;
                    }

                    candidates.Add(candidate);
                }

                bucket.Add(index);
            }

            foreach (var candidateIndex in candidates)
            {
                var candidate = comparable[candidateIndex];
                if (!HasComparableAspectRatio(record, candidate) ||
                    BitOperations.PopCount(hash ^ candidate.DifferenceHash!.Value) > SimilarityHammingThreshold)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(record.Sha256) &&
                    string.Equals(record.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                union.Join(index, candidateIndex);
            }
        }

        var similarGroups = Enumerable.Range(0, comparable.Length)
            .GroupBy(union.Find)
            .Select(group => group.Select(index => comparable[index]).ToArray())
            .Where(group => group.Length > 1)
            .OrderBy(group => group.Select(record => record.FilePath).Min(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < similarGroups.Length; index++)
        {
            var group = similarGroups[index];
            var groupId = $"S{index + 1:N0}";
            foreach (var record in group)
            {
                var current = result[record.FilePath];
                result[record.FilePath] = current with
                {
                    SimilarGroupId = groupId,
                    SimilarGroupCount = group.Length
                };
            }
        }

        return result;
    }

    public static int HammingDistance(ulong left, ulong right)
        => BitOperations.PopCount(left ^ right);

    private static bool HasComparableAspectRatio(
        ScreenshotAnalysisRecord left,
        ScreenshotAnalysisRecord right)
    {
        var leftRatio = left.PixelWidth / (double)left.PixelHeight;
        var rightRatio = right.PixelWidth / (double)right.PixelHeight;
        return Math.Abs(leftRatio - rightRatio) / Math.Max(leftRatio, rightRatio) <= 0.12;
    }

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public UnionFind(int count)
        {
            _parent = Enumerable.Range(0, count).ToArray();
            _rank = new byte[count];
        }

        public int Find(int value)
        {
            while (_parent[value] != value)
            {
                _parent[value] = _parent[_parent[value]];
                value = _parent[value];
            }

            return value;
        }

        public void Join(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot == rightRoot)
            {
                return;
            }

            if (_rank[leftRoot] < _rank[rightRoot])
            {
                _parent[leftRoot] = rightRoot;
            }
            else if (_rank[leftRoot] > _rank[rightRoot])
            {
                _parent[rightRoot] = leftRoot;
            }
            else
            {
                _parent[rightRoot] = leftRoot;
                _rank[leftRoot]++;
            }
        }
    }
}
