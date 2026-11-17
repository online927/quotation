using System.Text;
using Tally.Simulator;

namespace Quotation.Tally.Tests;

/// <summary>Signs in names (° ₹ × ± µ) come through from Tally however it answers; an older Tally is asked again in UTF-8 once.</summary>
public class TallyEncodingTests
{
    private const string Bevel = "187-901-10-UNIVERSAL BEVEL PROTRACTOR";

    private static async Task<string> BevelDescription(Sim sim)
    {
        await foreach (var item in new TallyMasterService(sim.Client()).GetStockItemsAsync())
            if (item.Name == Bevel) return item.Description;
        throw new InvalidOperationException("not found");
    }

    [Theory]
    [InlineData(SimulatorEncoding.SameAsRequest)]
    [InlineData(SimulatorEncoding.AnsiSaysUtf8)]
    [InlineData(SimulatorEncoding.NoUnicode)]
    public async Task Degree_sign_arrives_intact(SimulatorEncoding mode)
    {
        TallyXmlClient.ResetEncodingMemory();
        var sim = new Sim();
        sim.Handler.AnswerEncoding = mode;
        Assert.Contains("0-360°", await BevelDescription(sim));
        Assert.DoesNotContain('�', await BevelDescription(sim));
        TallyXmlClient.ResetEncodingMemory();
    }

    [Fact]
    public async Task Tally_is_asked_in_unicode()
    {
        TallyXmlClient.ResetEncodingMemory();
        var sim = new Sim();
        await BevelDescription(sim);
        Assert.All(sim.Handler.RequestCharsets, c => Assert.Equal("utf-16", c));
    }

    [Fact]
    public async Task An_older_tally_is_tried_in_unicode_only_once()
    {
        TallyXmlClient.ResetEncodingMemory();
        var sim = new Sim();
        sim.Handler.AnswerEncoding = SimulatorEncoding.NoUnicode;
        await BevelDescription(sim);
        await BevelDescription(sim);
        Assert.Equal(1, sim.Handler.RequestCharsets.Count(c => c == "utf-16"));
        TallyXmlClient.ResetEncodingMemory();
    }

    [Theory]
    [InlineData("utf-16", new byte[] { 0x3C, 0x00, 0x41, 0x00 }, "<A")]
    [InlineData("", new byte[] { 0x3C, 0x00, 0xB0, 0x00 }, "<°")]
    [InlineData("utf-8", new byte[] { 0x3C, 0xC2, 0xB0 }, "<°")]
    [InlineData("utf-8", new byte[] { 0x3C, 0xB0 }, "<°")] // says UTF-8, is ANSI
    [InlineData("", new byte[] { 0x3C, 0xE2, 0x82, 0xB9 }, "<₹")]
    [InlineData("", new byte[] { 0xFF, 0xFE, 0x3C, 0x00 }, "<")]
    public void Encoding_is_detected(string charset, byte[] bytes, string expected)
    {
        var enc = TallyXmlClient.DetectEncoding(bytes, charset);
        var text = enc.GetString(bytes);
        Assert.Equal(expected, text.TrimStart('﻿'));
    }
}
