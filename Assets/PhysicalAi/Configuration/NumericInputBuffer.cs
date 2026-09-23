// Copyright 2026
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhysicalAi.Configuration
{
    /// <summary>Preserves editing text (including incomplete decimals) independently of settings values.</summary>
    public sealed class NumericInputBuffer
    {
        readonly Dictionary<string, string> _text = new Dictionary<string, string>();
        readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        public void BeginDraw() => _errors.Clear(); // Only currently visible fields constrain submission.
        public void Clear() { _text.Clear(); _errors.Clear(); }
        public string Text(string field, object value)
        {
            if (!_text.TryGetValue(field, out var text))
                _text[field] = text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text;
        }
        public int Integer(string field, string text, int previous)
        {
            _text[field] = text;
            _errors.Remove(field);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
            _errors[field] = field + ": enter a whole number.";
            return previous;
        }
        public float Float(string field, string text, float previous)
        {
            _text[field] = text;
            _errors.Remove(field);
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)) return value;
            _errors[field] = field + ": enter a finite number (use a decimal point).";
            return previous;
        }
        public void Validate()
        {
            foreach (var error in _errors.Values) throw new ArgumentException(error);
        }
    }
}
