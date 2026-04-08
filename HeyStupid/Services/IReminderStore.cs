namespace HeyStupid.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using HeyStupid.Models;

    public interface IReminderStore
    {
        Task LoadAsync();
        List<Reminder> GetAll();
        Reminder? GetById(Guid id);
        Task SaveAsync(Reminder reminder);
        Task DeleteAsync(Guid id);
    }
}