async function fetchData(dataPath) {
    const pathResponse = await fetch(dataPath);
    const data = await pathResponse.json();
   // localStorage.setItem("data",JSON.stringify(data));
    return data;
};
export default async function getData(dataPath){
    return await /*JSON.parse(localStorage.getItem('data')) || */fetchData(dataPath);
}