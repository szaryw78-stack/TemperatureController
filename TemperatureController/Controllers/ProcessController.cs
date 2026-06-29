namespace TemperatureController.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using TemperatureController.Models;

    [ApiController]
    [Route("api/[controller]")]
    public class ProcessController : ControllerBase
    {
        private readonly ProcessStateManager _state;
        public ProcessController(ProcessStateManager state) => _state = state;

        [HttpPost("toggle")]
        public IActionResult Toggle()
        {
            _state.ToggleProcess();
            return Ok(new { recording = _state.IsRecording });
        }

        [HttpPost("comment")]
        public IActionResult UpdateComment([FromBody] CommentDto dto)
        {
            _state.CurrentComment = dto.Comment;
            return Ok();
        }
        [HttpPost("set-filename")]
        public IActionResult SetFileName([FromBody] FileNameDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Nazwa nie może być pusta");
            _state.CurrentFileName = dto.Name; // Dodaj też pole CurrentFileName w state
            return Ok();
        }
    }

    public class CommentDto { public string Comment { get; set; } }
    public class FileNameDto { public string Name { get; set; } }
}
