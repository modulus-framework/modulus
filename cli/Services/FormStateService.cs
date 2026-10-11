using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modulus.Ui.Services
{
    /// <summary>
    /// FormStateService - Server-side form state management
    /// Handles multi-step form data storage and retrieval
    /// </summary>
    public class FormStateService
    {
        private readonly Dictionary<string, FormStateSnapshot> _stateCache = new();
        private readonly int _maxCacheSize = 1000;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);

        /// <summary>
        /// Form state snapshot with metadata
        /// </summary>
        public class FormStateSnapshot
        {
            public string FormId { get; set; } = string.Empty;
            public Dictionary<string, object?> Data { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
            public int CurrentStep { get; set; } = 0;
            public Dictionary<int, bool> StepCompletion { get; set; } = new();
            public string SessionId { get; set; } = string.Empty;
            public bool IsValid { get; set; } = true;
        }

        /// <summary>
        /// Save form state
        /// </summary>
        public Task<bool> SaveFormStateAsync(string formId, string sessionId, FormStateSnapshot state)
        {
            try
            {
                if (string.IsNullOrEmpty(formId) || string.IsNullOrEmpty(sessionId))
                {
                    return Task.FromResult(false);
                }

                var key = GenerateStateKey(formId, sessionId);
                state.LastUpdatedAt = DateTime.UtcNow;

                // Check cache size and evict old entries if needed
                if (_stateCache.Count >= _maxCacheSize)
                {
                    EvictOldestState();
                }

                _stateCache[key] = state;
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Load form state
        /// </summary>
        public Task<FormStateSnapshot?> LoadFormStateAsync(string formId, string sessionId)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    // Check if expired
                    if (DateTime.UtcNow - state.CreatedAt > _cacheExpiration)
                    {
                        _stateCache.Remove(key);
                        return Task.FromResult<FormStateSnapshot?>(null);
                    }

                    return Task.FromResult<FormStateSnapshot?>(state);
                }

                return Task.FromResult<FormStateSnapshot?>(null);
            }
            catch
            {
                return Task.FromResult<FormStateSnapshot?>(null);
            }
        }

        /// <summary>
        /// Clear form state
        /// </summary>
        public Task<bool> ClearFormStateAsync(string formId, string sessionId)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);
                _stateCache.Remove(key);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Get specific field value from form state
        /// </summary>
        public Task<object?> GetFieldValueAsync(string formId, string sessionId, string fieldName)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    if (state.Data.TryGetValue(fieldName, out var value))
                    {
                        return Task.FromResult(value);
                    }
                }

                return Task.FromResult<object?>(null);
            }
            catch
            {
                return Task.FromResult<object?>(null);
            }
        }

        /// <summary>
        /// Set field value in form state
        /// </summary>
        public Task<bool> SetFieldValueAsync(string formId, string sessionId, string fieldName, object? value)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    state.Data[fieldName] = value;
                    state.LastUpdatedAt = DateTime.UtcNow;
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Get current step
        /// </summary>
        public Task<int> GetCurrentStepAsync(string formId, string sessionId)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    return Task.FromResult(state.CurrentStep);
                }

                return Task.FromResult(0);
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Set current step
        /// </summary>
        public Task<bool> SetCurrentStepAsync(string formId, string sessionId, int stepIndex)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    state.CurrentStep = stepIndex;
                    state.LastUpdatedAt = DateTime.UtcNow;
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Mark step as complete
        /// </summary>
        public Task<bool> MarkStepCompleteAsync(string formId, string sessionId, int stepIndex)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    state.StepCompletion[stepIndex] = true;
                    state.LastUpdatedAt = DateTime.UtcNow;
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Get step completion status
        /// </summary>
        public Task<bool> IsStepCompleteAsync(string formId, string sessionId, int stepIndex)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    return Task.FromResult(state.StepCompletion.ContainsKey(stepIndex) &&
                                           state.StepCompletion[stepIndex]);
                }

                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Validate form state structure
        /// </summary>
        public Task<(bool Valid, string[] Missing)> ValidateStateStructureAsync(
            string formId,
            string sessionId,
            string[] requiredFields)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    var missing = new List<string>();

                    foreach (var field in requiredFields)
                    {
                        if (!state.Data.ContainsKey(field) || state.Data[field] == null)
                        {
                            missing.Add(field);
                        }
                    }

                    return Task.FromResult((missing.Count == 0, missing.ToArray()));
                }

                return Task.FromResult((false, requiredFields));
            }
            catch
            {
                return Task.FromResult((false, requiredFields));
            }
        }

        /// <summary>
        /// Export form state as JSON
        /// </summary>
        public Task<string?> ExportFormStateAsJsonAsync(string formId, string sessionId)
        {
            try
            {
                var key = GenerateStateKey(formId, sessionId);

                if (_stateCache.TryGetValue(key, out var state))
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(state));
                }

                return Task.FromResult<string?>(null);
            }
            catch
            {
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// Import form state from JSON
        /// </summary>
        public Task<bool> ImportFormStateFromJsonAsync(string formId, string sessionId, string json)
        {
            try
            {
                var state = JsonSerializer.Deserialize<FormStateSnapshot>(json);

                if (state != null)
                {
                    state.FormId = formId;
                    state.SessionId = sessionId;
                    state.LastUpdatedAt = DateTime.UtcNow;

                    var key = GenerateStateKey(formId, sessionId);
                    _stateCache[key] = state;

                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Get all states for a form (across all sessions)
        /// </summary>
        public Task<List<FormStateSnapshot>> GetAllFormStatesAsync(string formId)
        {
            try
            {
                var results = new List<FormStateSnapshot>();
                var prefix = $"{formId}:";

                foreach (var kvp in _stateCache)
                {
                    if (kvp.Key.StartsWith(prefix))
                    {
                        results.Add(kvp.Value);
                    }
                }

                return Task.FromResult(results);
            }
            catch
            {
                return Task.FromResult(new List<FormStateSnapshot>());
            }
        }

        /// <summary>
        /// Clear all states for a form
        /// </summary>
        public Task<int> ClearAllFormStatesAsync(string formId)
        {
            try
            {
                var prefix = $"{formId}:";
                var removed = 0;

                var keysToRemove = new List<string>();
                foreach (var key in _stateCache.Keys)
                {
                    if (key.StartsWith(prefix))
                    {
                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _stateCache.Remove(key);
                    removed++;
                }

                return Task.FromResult(removed);
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public Task<(int TotalEntries, long ApproximateSizeBytes)> GetCacheStatsAsync()
        {
            try
            {
                var totalEntries = _stateCache.Count;
                long approxSize = 0;

                foreach (var state in _stateCache.Values)
                {
                    var json = JsonSerializer.Serialize(state);
                    approxSize += System.Text.Encoding.UTF8.GetByteCount(json);
                }

                return Task.FromResult((totalEntries, approxSize));
            }
            catch
            {
                return Task.FromResult((0, 0L));
            }
        }

        /// <summary>
        /// Clear expired states
        /// </summary>
        public Task<int> ClearExpiredStatesAsync()
        {
            try
            {
                var keysToRemove = new List<string>();
                var now = DateTime.UtcNow;

                foreach (var kvp in _stateCache)
                {
                    if (now - kvp.Value.CreatedAt > _cacheExpiration)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _stateCache.Remove(key);
                }

                return Task.FromResult(keysToRemove.Count);
            }
            catch
            {
                return Task.FromResult(0);
            }
        }

        /// <summary>
        /// Generate unique key for form state
        /// </summary>
        private string GenerateStateKey(string formId, string sessionId)
        {
            return $"{formId}:{sessionId}";
        }

        /// <summary>
        /// Evict oldest state when cache is full
        /// </summary>
        private void EvictOldestState()
        {
            if (_stateCache.Count == 0) return;

            var oldest = _stateCache.Values
                .OrderBy(s => s.LastUpdatedAt)
                .FirstOrDefault();

            if (oldest != null)
            {
                var keyToRemove = _stateCache
                    .FirstOrDefault(kvp => kvp.Value == oldest)
                    .Key;

                if (keyToRemove != null)
                {
                    _stateCache.Remove(keyToRemove);
                }
            }
        }
    }
}
