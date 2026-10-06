using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PokeApiManager : MonoBehaviour
{

    // boton de enter para buscar el pokemon

    private void Start()
    {
        // Se dispara cuando el usuario presiona Enter dentro del InputField
        if (inputPokemonName != null)
        {
            inputPokemonName.onSubmit.AddListener(OnInputSubmit);
        }
        // Ocultamos la imagen al iniciar para que no se vea el cuadro blanco
        if (imagePokemonSprite != null)
        {
            imagePokemonSprite.gameObject.SetActive(false);
        }

    }

    private void OnInputSubmit(string text)
    {
        OnSearchButtonClicked();
    }

    private void OnDestroy()
    {
        // Es buena práctica desuscribir el evento al destruir el objeto
        if (inputPokemonName != null)
        {
            inputPokemonName.onSubmit.RemoveListener(OnInputSubmit);
        }
    }

    [Header("UI References")]
    [SerializeField] private TMP_InputField inputPokemonName;
    [SerializeField] private TMP_Text textPokemonName;
    [SerializeField] private TMP_Text textPokemonInfo;
    [SerializeField] private Image imagePokemonSprite;

    private const string BaseUrl = "https://pokeapi.co/api/v2/pokemon/";

    // Método que podés asignar al OnClick de un botón en la UI
    public void OnSearchButtonClicked()
    {
        string pokemonName = inputPokemonName.text.Trim().ToLower();
        if (!string.IsNullOrEmpty(pokemonName))
        {
            StartCoroutine(GetPokemonData(pokemonName));
        }
    }

    private IEnumerator GetPokemonData(string pokemonName)
    {
        string url = BaseUrl + pokemonName;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                textPokemonName.text = "Error";
                textPokemonInfo.text = "No se encontró el Pokémon.";
            }
            else
            {
                // Parseamos el JSON
                string jsonResult = request.downloadHandler.text;
                PokemonData pokemon = JsonUtility.FromJson<PokemonData>(jsonResult);

                // Mostramos la info en TextMeshPro
                textPokemonName.text = $"#{pokemon.id} - {pokemon.name.ToUpper()}";

                string types = "";
                foreach (var t in pokemon.types)
                {
                    types += t.type.name + " ";
                }

                textPokemonInfo.text = $"Altura: {pokemon.height / 10f} m\n" +
                                       $"Peso: {pokemon.weight / 10f} kg\n" +
                                       $"Tipos: {types.Trim()}";

                // Opcional: cargar el sprite frontal
                if (pokemon.sprites != null && !string.IsNullOrEmpty(pokemon.sprites.front_default))
                {
                    StartCoroutine(DownloadSprite(pokemon.sprites.front_default));
                }
            }
        }
    }

    private IEnumerator DownloadSprite(string spriteUrl)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(spriteUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

                // Asignamos el sprite y activamos el GameObject
                imagePokemonSprite.sprite = newSprite;
                imagePokemonSprite.gameObject.SetActive(true);
            }
        }
    }
}

// Estructuras de datos para JsonUtility de Unity
[Serializable]
public class PokemonData
{
    public int id;
    public string name;
    public int height;
    public int weight;
    public Sprites sprites;
    public TypeSlot[] types;
}

[Serializable]
public class Sprites
{
    public string front_default;
}

[Serializable]
public class TypeSlot
{
    public TypeInfo type;
}

[Serializable]
public class TypeInfo
{
    public string name;
}