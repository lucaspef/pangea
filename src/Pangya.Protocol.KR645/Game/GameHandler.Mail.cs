using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Mail;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Correio (docs/protocolo/SPEC-correio-presentes.md). As respostas só valem dentro do My Room (o cliente trava a tela
/// até a resposta: sempre responder). Aviso de carta nova: 0x15E com as não lidas (no login, ao pedir com 0xE6/0x15E)
/// e 0x31 para o destinatário online.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CMailNickSearch = 0x07, CMailUnread = 0xE6, CMailUnreadAfterBox = 0x15E, CLegacyGiftTake = 0x24, CLegacyGiftReturn = 0x4D;
    const ushort SMailList = 0x140, SMailListError = 0x141, SMailRead = 0x142, SMailReadError = 0x143, SMailTaken = 0x144,
        SMailTakeError = 0x145, SMailDeleted = 0x15C, SMailDeleteError = 0x15D, SMailSent = 0x13E, SMailSendError = 0x13F,
        SNickFound = 0x9F, SUnreadMail = 0x15E, SNewMail = 0x31, SLegacyGiftTaken = 0x79, SLegacyGiftReturned = 0xDA;
    /// <summary>0x15D código 9: "tem anexo, não dá para apagar".</summary>
    const uint MailHasItems = 9;

    MailService? Mail => ctx.Mail;

    async ValueTask<bool> HandleMailAsync(PacketReader p)
    {
        if (Mail == null && p.Id is CMailList or CMailRead or CMailDelete or CMailTake or CMailSend or CMailNickSearch
            or CMailUnread or CMailUnreadAfterBox) return false;
        switch (p.Id)
        {
            case CMailList: await MailListAsync((int)p.U32()); return true;
            case CMailRead: await MailReadAsync((int)p.U32()); return true;
            case CMailTake: await MailTakeAsync((int)p.U32()); return true;
            case CMailDelete:
            {
                int n = (int)Math.Min(p.U32(), 100u);
                var ids = new List<int>(n);
                for (int i = 0; i < n && p.Remaining >= 4; i++) ids.Add((int)p.U32());
                p.Skip(p.Remaining);
                int failed = await Mail!.Store.DeleteAsync(Player.AccountId, ids);
                conn.Send(failed == 0 ? new PacketWriter(SMailDeleted) : new PacketWriter(SMailDeleteError).U32(MailHasItems));
                return true;
            }
            case CMailSend: await MailSendAsync(p); return true;
            case CMailNickSearch: await NickSearchAsync(p); return true;
            case CMailUnread or CMailUnreadAfterBox: p.Skip(p.Remaining); await SendUnreadMailAsync(); return true;
            case CLegacyGiftTake: p.Skip(p.Remaining); conn.Send(new PacketWriter(SLegacyGiftTaken).U32(1)); return true;   // caixa antiga: não existe no KR
            case CLegacyGiftReturn: { uint guid = p.U32(); p.Skip(p.Remaining); conn.Send(new PacketWriter(SLegacyGiftReturned).U32(guid)); return true; }
            default: return false;
        }
    }

    /// <summary>sMailIncludeItem de um anexo: id do objeto (ou da carta), tid, quantidade e upgrades do taco.</summary>
    static sMailIncludeItem Include(int id, int tid, int count, System.Text.Json.Nodes.JsonObject attrs)
    {
        var s = new sMailIncludeItem { dwIDX = (uint)id, dwTID = (uint)tid, iCount = count, dwSetTID = 0xFFFFFFFF };
        if (attrs["pcl"] is System.Text.Json.Nodes.JsonArray pcl)
            for (int i = 0; i < Math.Min(pcl.Count, 5); i++) s.soCom[i] = (short)(pcl[i]?.GetValue<int>() ?? 0);
        return s;
    }

    static sMailInfoBrief Brief(MailLetter m)
    {
        var b = new sMailInfoBrief { id = (uint)m.Id, bRead = (byte)(m.Read ? 1 : 0) };
        Cp949.Write(b.sender, m.SenderNick);
        int pending = 0;
        MailItem? first = null;
        foreach (var it in m.Items) if (!it.Taken) { pending++; first ??= it; }
        b.itemCount = pending;
        if (first != null) b.item = Include(first.Id, first.TypeId, first.Quantity, first.Attrs);
        return b;
    }

    /// <summary>0xBC u32 página -> 0x140 u32 páginas, u32 página, u32 n, n × sMailInfoBrief (20 por página, mais nova primeiro).</summary>
    async Task MailListAsync(int page)
    {
        page = Math.Max(page, 1);
        var (items, total) = await Mail!.Store.ListAsync(Player.AccountId, page, MailService.PerPage);
        int pages = Math.Max(1, (total + MailService.PerPage - 1) / MailService.PerPage);
        var w = new PacketWriter(SMailList, 16 + items.Count * 0xC2).U32((uint)pages).U32((uint)page).U32((uint)items.Count);
        foreach (var m in items) w.Struct(Brief(m));
        conn.Send(w);
    }

    /// <summary>0xBD u32 carta -> 0x142 u32 id, str remetente, str data, str texto, u8 tem anexos, [u32 n, n × anexo]; marca lida.</summary>
    async Task MailReadAsync(int id)
    {
        var m = await Mail!.Store.GetAsync(Player.AccountId, id);
        if (m == null) { conn.Send(new PacketWriter(SMailReadError).U8((byte)MailCode.CannotSend)); return; }
        await Mail.Store.MarkReadAsync(Player.AccountId, id);
        var pending = new List<MailItem>();
        foreach (var it in m.Items) if (!it.Taken) pending.Add(it);
        var w = new PacketWriter(SMailRead, 64 + m.Message.Length * 2 + pending.Count * 0x35).U32((uint)m.Id).Str(m.SenderNick)
            .Str(m.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Str(m.Message).U8((byte)(pending.Count > 0 ? 1 : 0));
        if (pending.Count > 0)
        {
            w.U32((uint)pending.Count);
            foreach (var it in pending) w.Struct(Include(it.Id, it.TypeId, it.Quantity, it.Attrs));
        }
        conn.Send(w);
    }

    /// <summary>
    /// 0xBF u32 carta -> 0x144 u32 n, n × anexo (id do objeto no inventário; empilháveis com o total novo). O pang (saco
    /// 0x1A000010) o cliente não põe: vai num 0xC6.
    /// </summary>
    async Task MailTakeAsync(int id)
    {
        var (ok, placed, pang) = await Mail!.TakeAsync(Player, id);
        if (!ok) { conn.Send(new PacketWriter(SMailTakeError).U8(1)); return; }
        var w = new PacketWriter(SMailTaken, 8 + placed.Count * 0x35).U32((uint)placed.Count);
        foreach (var (_, it) in placed) w.Struct(Include(it.Id, it.TypeId, it.IsConsumable ? it.Quantity : 1, it.Attrs));
        conn.Send(w);
        if (pang > 0) conn.Send(PangUpdate());
        Log.Info($"{conn} pegou {placed.Count} anexo(s) da carta {id}{(pang > 0 ? $" e {pang} pang" : "")}");
    }

    /// <summary>
    /// 0xBB u32 meu uid, u32 destino, u8 n, i64 taxa (ignorada: o servidor calcula), str "", str texto, n × anexo
    /// -> 0x13E u32 n, n × anexo (o que saiu do meu inventário) + 0xC6; erro 0x13F u8 código. Destinatário online: 0x31.
    /// </summary>
    async Task MailSendAsync(PacketReader p)
    {
        p.U32();
        long to = p.U32();
        int n = p.U8();
        p.U64();
        p.Str(64);
        string text = p.Str(1024);
        var attach = new List<(int, int, int)>();
        for (int i = 0; i < n && p.Remaining >= 0x35; i++)
        {
            var s = p.Struct<sMailIncludeItem>();
            attach.Add(((int)s.dwIDX, (int)s.dwTID, s.iCount));
        }
        p.Skip(p.Remaining);
        if (n > MailService.MaxItems) { conn.Send(new PacketWriter(SMailSendError).U8((byte)MailCode.TooManyItems)); return; }
        var (code, left) = await Mail!.SendAsync(Player, to, text, attach);
        if (code != MailCode.Ok) { conn.Send(new PacketWriter(SMailSendError).U8((byte)code)); return; }
        var w = new PacketWriter(SMailSent, 8 + attach.Count * 0x35).U32((uint)attach.Count);
        foreach (var (id, tid, count) in attach) w.Struct(Include(id, tid, Item.GroupOf(tid) is ItemGroup.Ball or ItemGroup.Usable ? count : 1, []));
        conn.Send(w);
        conn.Send(PangUpdate());
        NotifyNewMail(to);
        Log.Info($"{conn} mandou carta para {to} com {attach.Count} anexo(s)");
    }

    /// <summary>0x07 u8 0, str nick -> 0x9F u8 0, u32 uid, sPangYaUserInfo (ou u8 1 = não achou).</summary>
    async Task NickSearchAsync(PacketReader p)
    {
        p.U8();
        string nick = p.Str(32);
        var uid = await Mail!.Store.AccountByNickAsync(nick);
        var target = uid is { } id ? (ctx.World.Find(id)?.Player ?? await ctx.Players.LoadAsync(id)) : null;
        if (target == null) { conn.Send(new PacketWriter(SNickFound).U8(1)); return; }
        conn.Send(new PacketWriter(SNickFound, 0x120).U8(0).U32((uint)target.AccountId).Struct(PlayerStructs.UserInfo(target).info));
    }

    /// <summary>0x15E u32 n, n × sMailInfoBrief: não lidas (o botão de presente pisca se n > 0).</summary>
    async Task SendUnreadMailAsync()
    {
        if (Mail == null) return;
        var unread = await Mail.Store.UnreadAsync(Player.AccountId, MailService.UnreadMax);
        var w = new PacketWriter(SUnreadMail, 8 + unread.Count * 0xC2).U32((uint)unread.Count);
        foreach (var m in unread) w.Struct(Brief(m));
        conn.Send(w);
    }

    /// <summary>Carta nova para quem está online: 0x31 (o cliente pede a lista com 0xE6).</summary>
    void NotifyNewMail(long account)
    {
        if (ctx.World.Find(account) is GameHandler h) h.Connection.Send(new PacketWriter(SNewMail));
    }
}
