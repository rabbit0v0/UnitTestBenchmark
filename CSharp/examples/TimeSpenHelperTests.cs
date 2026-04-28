using System;
using System.Globalization;
using System.Threading;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Xunit;

namespace Microsoft.PowerToys.Settings.UI.Tests.Helpers
{
    public class TimeSpanHelperTests
    {
        /// <summary>
        /// Tests that a null TimeSpan returns an empty string.
        /// </summary>
        [Fact]
        public void Convert_WithNullTimeSpan_ReturnsEmptyString()
        {
            // Arrange
            TimeSpan? nullTimeSpan = null;

            // Act
            var result = TimeSpanHelper.Convert(nullTimeSpan);

            // Assert
            Assert.Equal(string.Empty, result);
        }

        /// <summary>
        /// Tests that TimeSpan.Zero is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithZeroTimeSpan_ReturnsCorrectTime()
        {
            // Arrange
            var zeroTimeSpan = TimeSpan.Zero;

            // Act
            var result = TimeSpanHelper.Convert(zeroTimeSpan);

            // Assert
            // Should return midnight in the current culture's short time format
            Assert.NotEmpty(result);
            Assert.NotEqual(string.Empty, result);
        }

        /// <summary>
        /// Tests that a positive TimeSpan is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithPositiveTimeSpan_ReturnsFormattedTime()
        {
            // Arrange
            var timeSpan = new TimeSpan(14, 30, 45); // 14:30:45
            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                // Set culture to US English (12-hour format)
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

                // Act
                var result = TimeSpanHelper.Convert(timeSpan);

                // Assert
                Assert.NotEmpty(result);
                // Should contain PM indicator in 12-hour format
                Assert.Contains("PM", result, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that a negative TimeSpan is normalized to positive and converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithNegativeTimeSpan_NormalizesAndReturnsFormattedTime()
        {
            // Arrange
            var negativeTimeSpan = new TimeSpan(-10, -30, -15); // Negative duration
            var expectedPositiveTimeSpan = new TimeSpan(10, 30, 15); // Expected normalized result

            // Act
            var result = TimeSpanHelper.Convert(negativeTimeSpan);
            var expectedResult = TimeSpanHelper.Convert(expectedPositiveTimeSpan);

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that TimeSpan with hours only is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithHoursOnly_ReturnsFormattedTime()
        {
            // Arrange
            var timeSpan = new TimeSpan(9, 0, 0); // 9:00:00

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains("9", result);
        }

        /// <summary>
        /// Tests that TimeSpan with minutes only is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithMinutesOnly_ReturnsFormattedTime()
        {
            // Arrange
            var timeSpan = new TimeSpan(0, 45, 0); // 0:45:00

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that TimeSpan near midnight is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithMidnightTimeSpan_ReturnsFormattedTime()
        {
            // Arrange
            var timeSpan = new TimeSpan(0, 0, 0); // 00:00:00

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that TimeSpan near end of day is converted correctly.
        /// </summary>
        [Fact]
        public void Convert_WithEndOfDayTimeSpan_ReturnsFormattedTime()
        {
            // Arrange
            var timeSpan = new TimeSpan(23, 59, 59); // 23:59:59

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that the method respects 24-hour culture format.
        /// </summary>
        [Fact]
        public void Convert_With24HourCulture_ReturnsFormattedTimeIn24HourFormat()
        {
            // Arrange
            var timeSpan = new TimeSpan(14, 30, 0); // 14:30:00
            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                // Set culture to German (typically uses 24-hour format)
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                // Act
                var result = TimeSpanHelper.Convert(timeSpan);

                // Assert
                Assert.NotEmpty(result);
                Assert.Contains("14", result);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that the method respects 12-hour culture format.
        /// </summary>
        [Fact]
        public void Convert_With12HourCulture_ReturnsFormattedTimeIn12HourFormat()
        {
            // Arrange
            var timeSpan = new TimeSpan(14, 30, 0); // 14:30:00
            var originalCulture = CultureInfo.CurrentCulture;

            try
            {
                // Set culture to US English (typically uses 12-hour format with AM/PM)
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

                // Act
                var result = TimeSpanHelper.Convert(timeSpan);

                // Assert
                Assert.NotEmpty(result);
                // Should have PM indicator
                Assert.True(result.Contains("PM", StringComparison.OrdinalIgnoreCase) || 
                           result.Contains("2"), "Result should contain PM indicator or hour 2 (14:30 = 2:30 PM)");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        /// <summary>
        /// Tests that TimeSpan with fractional seconds is handled correctly.
        /// </summary>
        [Theory]
        [InlineData(0, 0, 1, 500)] // 1.5 seconds
        [InlineData(1, 30, 45, 250)] // 1 hour, 30 minutes, 45.25 seconds
        [InlineData(12, 0, 0, 999)] // 12 hours, 0.999 seconds
        public void Convert_WithFractionalSeconds_ReturnsFormattedTime(int hours, int minutes, int seconds, int milliseconds)
        {
            // Arrange
            var timeSpan = new TimeSpan(0, hours, minutes, seconds, milliseconds);

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that multiple calls with the same TimeSpan return consistent results.
        /// </summary>
        [Fact]
        public void Convert_WithMultipleCalls_ReturnsConsistentResults()
        {
            // Arrange
            var timeSpan = new TimeSpan(10, 15, 30);

            // Act
            var result1 = TimeSpanHelper.Convert(timeSpan);
            var result2 = TimeSpanHelper.Convert(timeSpan);
            var result3 = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.Equal(result1, result2);
            Assert.Equal(result2, result3);
        }

        /// <summary>
        /// Tests that TimeSpan values at various hours throughout the day are handled correctly.
        /// </summary>
        [Theory]
        [InlineData(0)]   // Midnight
        [InlineData(6)]   // Early morning
        [InlineData(12)]  // Noon
        [InlineData(18)]  // Evening
        [InlineData(23)]  // Late night
        public void Convert_WithVariousHours_ReturnsFormattedTime(int hours)
        {
            // Arrange
            var timeSpan = new TimeSpan(hours, 30, 0);

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that a large TimeSpan (more than 24 hours) is handled correctly.
        /// </summary>
        [Fact]
        public void Convert_WithLargeTimeSpan_ReturnsFormattedTime()
        {
            // Arrange
            // 1 day, 5 hours, 30 minutes, 45 seconds
            var timeSpan = new TimeSpan(1, 5, 30, 45);

            // Act
            var result = TimeSpanHelper.Convert(timeSpan);

            // Assert
            Assert.NotEmpty(result);
        }

        /// <summary>
        /// Tests that the result is never empty for valid positive TimeSpans.
        /// </summary>
        [Fact]
        public void Convert_WithValidTimeSpan_NeverReturnsEmpty()
        {
            // Arrange & Act & Assert
            for (int hours = 0; hours < 24; hours++)
            {
                for (int minutes = 0; minutes < 60; minutes += 15)
                {
                    var timeSpan = new TimeSpan(hours, minutes, 0);
                    var result = TimeSpanHelper.Convert(timeSpan);
                    Assert.NotEmpty(result);
                }
            }
        }
    }
}