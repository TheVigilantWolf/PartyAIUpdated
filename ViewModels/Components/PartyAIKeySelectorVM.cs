using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PartyAIControls.ViewModels.Components
{
  public class PartyAIKeySelectorVM : ViewModel
  {
    private InputKey _modifierKey = InputKey.Invalid;
    private InputKey _key;
    private bool _hasModifier;

    internal InputKey ModifierKey
    {
      get => _modifierKey;
      private set
      {
        _modifierKey = value;
        OnPropertyChanged("ModifierKeyText");
      }
    }
    internal InputKey Key
    {
      get => _key;
      private set
      {
        _key = value;
        OnPropertyChanged("KeyText");
      }
    }

    public PartyAIKeySelectorVM(InputKey modifierKey = InputKey.Invalid, InputKey key = InputKey.Invalid, bool hasModifier = false)
    {
      Key = key;
      ModifierKey = modifierKey;
      HasModifier = hasModifier;
    }

    [DataSourceProperty] public string ModifierKeyText => ModifierKey.ToString();
    [DataSourceProperty] public string KeyText => Key.ToString();
    [DataSourceProperty]
    public bool HasModifier
    {
      get => _hasModifier;
      set
      {
        _hasModifier = value;
        OnPropertyChanged("HasModifier");
      }
    }

    public void EditKey()
    {
      List<InputKey> keys = Enum.GetValues(typeof(InputKey)).Cast<InputKey>().ToList().Where(k => ((int)k >= 12 && (int)k <= 88) || (int)k >= 227).ToList();
      keys.Add(InputKey.RightAlt);
      keys.Add(InputKey.RightShift);
      keys.Add(InputKey.RightControl);
      SelectKey(keys, (InputKey result) =>
      {
        Key = result;
      });
    }

    public void EditModifierKey()
    {
      InputKey[] keys = new InputKey[] { InputKey.LeftShift, InputKey.RightShift, InputKey.LeftAlt, InputKey.RightAlt, InputKey.LeftControl, InputKey.RightControl, InputKey.Invalid };
      SelectKey(keys.ToList(), (InputKey result) =>
      {
        ModifierKey = result;
      });
    }

    private void SelectKey(List<InputKey> keys, Action<InputKey> successCallback = null)
    {
      string title = new TextObject("{=PAIekKoDXkq}Select a Key").ToString();
      List<InquiryElement> list = keys.OrderBy(k => k.ToString()).ToList().ConvertAll(k => new InquiryElement(k, k.ToString(), null));
      MBInformationManager.ShowMultiSelectionInquiry(new(title, string.Empty, list, true, 1, 1, GameTexts.FindText("str_ok").ToString(), GameTexts.FindText("str_cancel").ToString(), (List<InquiryElement> results) =>
      {
        if (results.FirstOrDefault()?.Identifier is InputKey key)
        {
          successCallback?.Invoke(key);
        }
      }, null, isSeachAvailable: true));
    }
  }
}
