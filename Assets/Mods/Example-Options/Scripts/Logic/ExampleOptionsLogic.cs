using BAModAPI;
using BigAmbitions.Mods;

namespace SliderInMyDMs
{
    public class ExampleOptionsLogic
    {
        private const string ToggleSaveKey = "example_toggle";
        private const string DropdownSaveKey = "example_dropdown";
        private const string SliderSaveKey = "example_slider";
        private const string SliderNoSuffixSaveKey = "example_slider_no_suffix";

        private static readonly string[] DropdownChoices =
        {
            "sliderinmydms_dropdown_choice_one",
            "sliderinmydms_dropdown_choice_two",
            "sliderinmydms_dropdown_choice_three"
        };

        private ModContext _context = null!;
        private int _dropdownIndex;
        private int _sliderNoSuffixValue = 5;
        private int _sliderValue;
        private bool _toggleValue = true;

        public void Initialize(ModContext context)
        {
            _context = context;

            var options =
                new ModOptions()
                    .AddHeader("sliderinmydms_options_header")
                    .AddToggle(ToggleSaveKey, "sliderinmydms_toggle_label", _toggleValue, OnToggleValueChanged)
                    .AddSlider(SliderSaveKey, "sliderinmydms_slider_label", -100, 100, _sliderValue,
                        OnSliderValueChanged, "sliderinmydms_slider_value")
                    .AddSlider(SliderNoSuffixSaveKey, "sliderinmydms_slider_no_suffix_label", 0, 10,
                        _sliderNoSuffixValue, OnSliderNoSuffixValueChanged)
                    .AddDropdown(DropdownSaveKey, "sliderinmydms_dropdown_label", DropdownChoices, _dropdownIndex,
                        OnDropdownValueChanged)
                    .AddSplitter();

            OptionsService.Register(context.ModId, options);
            context.Logger.Info("Options registered.");
        }

        public void Shutdown()
        {
            OptionsService.RemoveModOptions(_context.ModId);
            _context.Logger.Info("Options unregistered.");
        }

        private void OnToggleValueChanged(bool value)
        {
            _toggleValue = value;
            _context.Logger.Info($"Toggle changed to = {value}");
        }

        private void OnDropdownValueChanged(int value)
        {
            _dropdownIndex = value;
            _context.Logger.Info($"Dropdown changed to index = {value}");
        }

        private void OnSliderValueChanged(int value)
        {
            _sliderValue = value;
            _context.Logger.Info($"Slider changed to = {value}");
        }

        private void OnSliderNoSuffixValueChanged(int value)
        {
            _sliderNoSuffixValue = value;
            _context.Logger.Info($"No-suffix slider changed to = {value}");
        }
    }
}